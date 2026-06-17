# InMemoryStore: Hash Table Unsafe

> Documentación interna de la hash table que reemplazó a `ConcurrentDictionary`.

---

## 1. Estructura general

```
┌─────────────────────────────────────────────────────────┐
│ InMemoryStore                                           │
├─────────────────────────────────────────────────────────┤
│ _buckets: Node?[1024..N]          ← array de buckets   │
│ _locks:   Lock[1024..N]           ← un lock por bucket  │
│ _count:   int                     ← entradas vivas      │
│ _bucketCount: int                 ← tamaño del array    │
│ _resizing: int (0/1)              ← flag de resize      │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Bucket[0] ──► Node ──► Node ──► null                  │
│  Bucket[1] ──► null                                     │
│  Bucket[2] ──► Node ──► null                            │
│  ...                                                     │
│                                                          │
│  Cada Node:                                              │
│  ┌──────────────────────┐                                │
│  │ Next  → Node?        │  managed ref (GC)             │
│  │ Hash  → uint         │  cache del hash               │
│  │ KeyLen → int          │                               │
│  │ Key   → byte*        │  NativeMemory.Alloc (no GC)  │
│  │ Value → StoreEntry   │  struct inline                │
│  └──────────────────────┘                                │
└─────────────────────────────────────────────────────────┘
```

---

## 2. Por qué `byte*` y no `byte[]`

| | `byte[]` (antes) | `byte*` (ahora) |
|---|---|---|
| **Alloc en GET** | `ToArray()` → 1 alloc | 0 alloc |
| **Alloc en SET** | `ToArray()` + `new byte[]` para value | `NativeMemory.Alloc` + `value.ToArray()` |
| **Hash** | `ByteArrayComparer.GetHashCode()` | `Hash(ReadOnlySpan<byte>)` inline |
| **Igualdad** | `SequenceEqual()` via comparer | `SequenceEqual()` directo |
| **GC** | El array es basura al borrar | `NativeMemory.Free()` — sin GC |

La clave está en `GET`: con `ConcurrentDictionary`, buscar `B("foo")` alocaba un `byte[3]` temporal. Ahora `Find(head, span, hash)` compara el span directamente contra `node->Key` sin alocar nada.

---

## 3. Concurrencia

```
Thread A: GET "foo"          Thread B: SET "bar" v
  hash("foo") = 0x1234         hash("bar") = 0xABCD
  bucket = 0x1234 & 1023      bucket = 0xABCD & 1023
         = 564                       = 789
  lock(_locks[564])            lock(_locks[789])
     ↓                             ↓
  Find en bucket 564           Insert en bucket 789
     ↓                             ↓
  unlock                       unlock

Sin contención. Solo compiten si caen en el mismo bucket.
```

---

## 4. Resize

```
1. Thread detecta _count >= _bucketCount * 0.75
2. Interlocked.CompareExchange(_resizing, 1, 0)
   → solo UN thread entra a Grow()
   → los demás hacen SpinWait hasta que _resizing = 0

3. Grow():
   a. Toma TODOS los locks (0..oldCount-1)
   b. Crea nuevo array de buckets (×2)
   c. Reubica cada Node en su nuevo bucket (hash & (newCount-1))
   d. Crea nuevos locks para los buckets extra
   e. Reemplaza _buckets y _locks
   f. Libera todos los locks

4. _resizing = 0 → los threads que esperaban siguen
```

---

## 5. Operaciones clave

### `GET` — 0 allocs

```
key.Span → Hash(span) → bucket = hash & mask
lock(bucket)
  Find(head, span, hash)
    → recorre la cadena: compara hash, luego SequenceEqual
    → O(1) promedio, O(n) peor caso
  si encontrado y no expirado → devuelve (byte[])Value
unlock
```

### `SET` — 1 alloc (Node) + 1 alloc (value)

```
key.Span → Hash(span)
EnsureCapacity()           ← puede disparar Grow
bucket = hash & mask
lock(bucket)
  Find(head, span, hash)
  si existe → actualiza Value (0 allocs extra)
  si no → Insert:
    Node n = new Node {
      Key = NativeMemory.Alloc(keyLen),
      Value = StoreEntry.FromString(value.ToArray()),
      Hash = hash,
      Next = head
    }
    Copia key a n.Key
    head = n
    _count++
unlock
```

### `DELETE` — 0 allocs para lookup

```
key.Span → Hash → bucket → lock → Find
si encontrado:
  desvincula Node de la cadena
  NativeMemory.Free(node.Key)   ← libera la clave nativa
  _count--
unlock
```

---

## 6. TTL (Time-To-Live)

- **Lazy**: `GET`, `EXISTS`, `TTL`, `TYPE` verifican `Value.IsExpired`. Si expiró, borran la entrada y devuelven null/0/-2.
- **Active**: `RunExpirationLoop` cada 100ms toma 20 keys al azar, borra las expiradas. Si >25% expiradas, repite inmediatamente.

Igual que Redis.

---

## 7. Hash (xxHash32 rolling)

```csharp
static uint Hash(ReadOnlySpan<byte> data)
{
    uint h = 0;
    foreach (byte b in data)
        h = ((h << 5) + h + b) ^ 0x9E3779B1;
    return h;
}
```

Simple, rápido, sin dependencias. La constante `0x9E3779B1` es la proporción áurea en binario.

---

## 8. Enteros sin strings

`INCR`/`DECR` convierten el valor sin pasar por `string`:
- `ToBytes(long)` → escribe dígitos ASCII directo a `byte[]`. 1 alloc (antes 2).
- `TryParseLong(byte[], out long)` → parsea desde `ReadOnlySpan<byte>`. 0 allocs (antes 1 string).

## 9. Comparación con el `ConcurrentDictionary` anterior

| | Antes | Ahora |
|---|---|---|
| **GET** | `ToArray()` → 1 alloc | 0 alloc |
| **SET** | `ToArray()` + `value.ToArray()` = 2 | `new Node()` + `value.ToArray()` + `NativeMemory.Alloc(key)` = 2 GC + 1 nativo |
| **EXISTS** | 1 alloc | 0 alloc |
| **DEL** | 1 alloc | 0 alloc |
| **INCR** | `ToArray()` + `GetString()` + `GetBytes()` = 3 | `NativeMemory.Alloc(key)` + `ToBytes(long)` = 2 (sin strings) |
| **KEYS** | N allocs (ToKey por key) | 0 allocs (salvo el resultado) |
| **Resize** | Interno del diccionario | Controlado por nosotros |
| **Contención** | Lock-free por bucket (más rápido) | Lock por bucket (un poco más lento, más simple) |

La gran mejora está en el path de lectura: 0 allocs en `GET`, que es ~80% del tráfico típico.
