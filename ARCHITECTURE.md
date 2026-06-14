# Arquitectura: Key-Value Store RESP2

> Documento de arquitectura con diagramas y explicación detallada de cada capa.

---

## 1. Estructura del proyecto

```
key-value-store/
├── src/KeyValueStore.Server/
│   ├── Program.cs                    # Entry point, configuración
│   ├── CommandDispatcher.cs          # Ruteo comando → handler
│   ├── Glob.cs                       # Pattern matching (* y ?) para KEYS/PSUBSCRIBE
│   ├── IReplicationCoordinator.cs    # Interfaz para replicación (stub)
│   ├── Commands/
│   │   ├── HashCommands.cs           # HSET, HGET, HDEL, HGETALL, HEXISTS, HLEN
│   │   ├── KeyCommands.cs            # DEL, EXISTS, KEYS, DBSIZE, FLUSHALL, etc.
│   │   ├── PubSubCommands.cs         # PUBLISH, SUBSCRIBE, UNSUBSCRIBE, PSUBSCRIBE
│   │   ├── ServerCommands.cs         # PING, ECHO, QUIT, HELLO, CLIENT
│   │   ├── SetCommands.cs            # SADD, SREM, SMEMBERS, SISMEMBER, SCARD
│   │   └── StringCommands.cs         # SET, GET, INCR, DECR, SETEX, PSETEX
│   ├── Exceptions/
│   │   ├── ProtocolException.cs
│   │   └── QuitException.cs
│   ├── Networking/
│   │   ├── ClientSession.cs          # Estado por conexión, loop de comandos + push
│   │   └── KvServer.cs               # TcpListener, accept loop, fire-and-forget
│   ├── PubSub/
│   │   ├── PubSubHub.cs              # Canales, patrones, publish, subscribe
│   │   └── PubSubTypes.cs            # PubSubMessage, SubscriptionMode
│   ├── Resp/
│   │   ├── RespReader.cs             # RESP2 → ReadOnlyMemory<byte>[]
│   │   └── RespWriter.cs             # ReadOnlyMemory<byte> → RESP2
│   └── Store/
│       ├── ByteArrayComparer.cs      # IEqualityComparer<byte[]> (SIMD)
│       ├── InMemoryStore.cs          # ConcurrentDictionary + TTL + operaciones
│       └── StoreEntry.cs             # Valor + tipo (string/set/hash) + TTL
└── tests/KeyValueStore.Tests/
    ├── CommandDispatcherTests.cs     # 42 tests de ruteo
    ├── IntegrationTests.cs           # 18 tests TCP real
    ├── ServerFixture.cs              # xUnit fixture compartido (1 servidor)
    ├── StackExchangeRedisCompatibilityTests.cs  # 29 tests SE.Redis
    ├── PubSub/
    │   └── PubSubCommandsTests.cs    # 15 tests de pub/sub
    ├── Resp/
    │   ├── RespReaderTests.cs        # 12 tests de parseo
    │   └── RespWriterTests.cs        # 17 tests de serialización
    └── Store/
        └── InMemoryStoreTests.cs     # 52 tests del store
```

---

## 2. Diagrama de arquitectura

```mermaid
flowchart TB
    subgraph Clients["Clientes"]
        CLI[redis-cli]
        SER[StackExchange.Redis]
    end

    subgraph Server["KvServer"]
        direction TB
        TL[TcpListener<br/>Accept loop]

        subgraph Sessions["ClientSession × N (fire-and-forget)"]
            direction LR
            RR["RespReader<br/>NetworkStream → ReadOnlyMemory&lt;byte&gt;[]<br/>ArrayPool + zero-copy"]
            CD["CommandDispatcher<br/>ASCII(command) → handler<br/>30 comandos"]
            RW["RespWriter<br/>ReadOnlyMemory&lt;byte&gt; → bytes<br/>ArrayBufferWriter"]
            RR --> CD --> RW
        end

        subgraph PubSub["PubSubHub"]
            CH["ConcurrentDictionary&lt;channel, subscribers&gt;"]
            PT["ConcurrentDictionary&lt;session, pattern&gt;"]
            IB["Channel&lt;PubSubMessage&gt;<br/>por suscriptor"]
        end

        subgraph Store["InMemoryStore"]
            IMS["ConcurrentDictionary&lt;byte[], StoreEntry&gt;<br/>ByteArrayComparer<br/>TTL lazy + active"]
        end

        TL --> Sessions
        CD --> IMS
        CD --> PubSub
        PubSub --> Sessions
    end

    CLI -->|TCP :6379| TL
    SER -->|TCP :6379| TL
    RW -->|TCP :6379| CLI
    RW -->|TCP :6379| SER
```

---

## 3. Flujo de un comando (SET/GET)

```mermaid
sequenceDiagram
    participant C as Cliente (redis-cli)
    participant CS as ClientSession
    participant RR as RespReader
    participant CD as CommandDispatcher
    participant IMS as InMemoryStore
    participant RW as RespWriter

    C->>CS: TCP: *3 $3 SET $3 foo $3 bar
    CS->>RR: ReadCommand(NetworkStream)
    Note over RR: ReadAsync → buffer[ArrayPool]<br/>Parse '*' → array count<br/>Parse '$' × N → spans
    RR-->>CS: ["SET", "foo", "bar"]
    CS->>CD: ExecuteAsync(args, writer, session)
    Note over CD: ASCII(args[0]).ToUpper() → "SET"<br/>_handlers["SET"] → StringCommands.Set
    CD->>IMS: Set("foo", "bar")
    Note over IMS: _store["foo"] = StoreEntry.FromString("bar")<br/>ConcurrentDictionary, lock-free
    CD->>RW: WriteOk()
    Note over RW: WriteByte('+') + "OK" + CRLF<br/>ArrayBufferWriter → FlushAsync
    RW-->>C: TCP: +OK
```

---

## 4. Flujo de Pub/Sub

```mermaid
sequenceDiagram
    participant Sub as Suscriptor
    participant Pub as Publicador
    participant Hub as PubSubHub
    participant CS_Sub as ClientSession (sub)
    participant CS_Pub as ClientSession (pub)

    rect rgb(25, 25, 50)
        Note over Sub,CS_Sub: Fase 1 — Suscripción
        Sub->>CS_Sub: SUBSCRIBE orders
        CS_Sub->>Hub: Subscribe("orders", session)
        Note over Hub: _channels["orders"][sessionId] = session
        CS_Sub->>Hub: EnterSubscriptionMode(Channel)
        CS_Sub-->>Sub: *3: subscribe, orders, :1
        Note over CS_Sub: Entra ReceivePushLoop<br/>Espera inbox o comandos socket
    end

    rect rgb(25, 50, 25)
        Note over Pub,CS_Pub: Fase 2 — Publicación
        Pub->>CS_Pub: PUBLISH orders "hola"
        CS_Pub->>Hub: Publish("orders", "hola")
        Note over Hub: Itera _channels["orders"]<br/>session.TryPush(message) → inbox
        Hub-->>CS_Sub: Channel.Writer.TryWrite(msg)
        CS_Pub-->>Pub: :1
    end

    rect rgb(50, 25, 25)
        Note over Sub,CS_Sub: Fase 3 — Entrega
        Note over CS_Sub: inbox.Reader.TryRead → msg
        CS_Sub->>CS_Sub: WritePush(msg)
        CS_Sub-->>Sub: *3: message, orders, hola
    end
```

### Formato de los mensajes Pub/Sub

Cada push que recibe el suscriptor es un array RESP2 con esta estructura:

| Posición | `message` | `pmessage` | `subscribe` | `unsubscribe` |
|---|---|---|---|---|
| `1)` | `"message"` | `"pmessage"` | `"subscribe"` | `"unsubscribe"` |
| `2)` | Canal | Patrón | Canal | Canal |
| `3)` | Payload | Canal | Count | Count |
| `4)` | — | Payload | — | — |

Ejemplo real con `redis-cli`:

```
1) "subscribe"       ← tipo
2) "orders"          ← canal
3) (integer) 1       ← count de suscriptores
1) "message"         ← tipo
2) "orders"          ← canal
3) "hola mundo"      ← payload
```

---

## 5. Pipeline de datos binary-safe

```mermaid
flowchart LR
    subgraph "Lectura (zero-copy)"
        S1[Socket] -->|ReadAsync| B1["byte[] buffer<br/>(ArrayPool)"]
        B1 -->|"Slice (sin copia)"| M1["ReadOnlyMemory&lt;byte&gt;[]<br/>args[0]=SET<br/>args[1]=key<br/>args[2]=value"]
    end

    subgraph "Store (owned copy)"
        M1 -->|"args[i].ToArray()"| K["byte[] key"]
        M1 -->|"args[i].ToArray()"| V["byte[] value"]
        K --> CD["ConcurrentDictionary<br/>ByteArrayComparer<br/>Span.SequenceEqual (SIMD)"]
        V --> CD
    end

    subgraph "Escritura (zero-alloc)"
        CD -->|respuesta| AW["ArrayBufferWriter&lt;byte&gt;"]
        AW -->|"Flush"| S2[Socket]
    end
```

Sin conversiones `byte[] ↔ string` en ningún punto del hot path. `Encoding.ASCII` solo se usa para nombres de comando y metadata.

---

## 6. Modelo de concurrencia

```mermaid
flowchart TB
    subgraph "KvServer (1 Task)"
        AL["AcceptTcpClientAsync loop"]
    end

    AL -->|fire-and-forget| CSA["ClientSession A<br/>RunAsync"]
    AL -->|fire-and-forget| CSB["ClientSession B<br/>RunAsync"]
    AL -->|fire-and-forget| CSC["ClientSession C<br/>RunAsync"]

    subgraph "Background"
        EXP["RunExpirationLoop<br/>100ms sampling"]
    end

    CSA --> IMS["InMemoryStore<br/>ConcurrentDictionary<br/>único punto de estado compartido"]
    CSB --> IMS
    CSC --> IMS
    EXP --> IMS
```

- Cada `ClientSession` es un `Task` independiente (IOCP, sin hilos bloqueados).
- `InMemoryStore` usa `ConcurrentDictionary` — lecturas lock-free, escrituras por bucket.
- `PubSubHub` usa `ConcurrentDictionary` para canales y patrones.
- Excepciones atrapadas dentro de `RunAsync`, nunca llegan al accept loop.

---

## 7. TTL — Doble expiración

```mermaid
flowchart TB
    subgraph "Lazy (por acceso)"
        A1["GET key"] --> C1{"IsExpired?"}
        C1 -->|sí| R1["TryRemove + null"]
        C1 -->|no| R2["Retornar valor"]
    end

    subgraph "Active (background)"
        A2["Cada 100ms"] --> S["Samplear 20 keys al azar"]
        S --> C2{"&gt;25% expiradas?"}
        C2 -->|sí| L["Repetir inmediatamente"]
        C2 -->|no| W["Esperar 100ms"]
        L --> S
        W --> A2
    end
```

---

## 8. StackExchange.Redis — Compatibilidad

| Categoría | Comandos | Estado |
|---|---|---|
| Handshake | `HELLO`, `CLIENT SETNAME/SETINFO/ID` | ✅ Implementados |
| Strings | `SET`, `GET`, `SETEX`, `PSETEX`, `INCR`, `DECR` | ✅ |
| Keys | `DEL`, `EXISTS`, `KEYS`, `TTL`, `PTTL`, `EXPIRE`, `TYPE` | ✅ |
| Sets | `SADD`, `SREM`, `SMEMBERS`, `SISMEMBER`, `SCARD` | ✅ |
| Hashes | `HSET`, `HGET`, `HDEL`, `HGETALL`, `HEXISTS`, `HLEN` | ✅ |
| Pub/Sub | `PUBLISH`, `SUBSCRIBE`, `PSUBSCRIBE`, `UNSUBSCRIBE` | ✅ |
| Clustering | `CLUSTER NODES`, `CONFIG GET`, `INFO`, `SENTINEL MASTERS` | ⚠️ `-ERR` (SE.Redis lo tolera) |
| Interno | `__Booksleeve_MasterChanged` | ⚠️ Suscripción aceptada, sin publicaciones (sin Sentinel) |

---

## 9. Decisiones clave

| Decisión | Alternativa rechazada | Razón |
|---|---|---|
| RESP2 | protocolo propio | Compatibilidad universal con clientes Redis existentes |
| `ConcurrentDictionary` | `Dictionary` + `lock` | Lecturas lock-free, sin deadlocks, más simple |
| `async`/`await` + IOCP | Event loop single-threaded (como redis) | Comandos lentos no bloquean a otros clientes |
| `byte[]` para keys y valores | `string` | Binary-safe real, sin pérdida de datos, zero-copy en RespReader |
| `ArrayPool<byte>` en RespReader |  | Menos presión de GC, buffers reutilizados |
| `ArrayBufferWriter<byte>` en RespWriter | `StringBuilder` → `string` → `byte[]` | Dos allocs menos por respuesta |
| `ByteArrayComparer` con `SequenceEqual` | `Enumerable.SequenceEqual` | SIMD-accelerated, misma performance que `string.Equals` |
