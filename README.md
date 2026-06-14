# KeyValueStore

Base de datos clave-valor en memoria con protocolo RESP2. Compatible con cualquier cliente Redis.

## Documentación

| Documento | Contenido |
|---|---|
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Diagramas de arquitectura, flujo de comandos y pub/sub, pipeline binary-safe, modelo de concurrencia, TTL, matriz de compatibilidad con SE.Redis |
| [`DESIGN.md`](DESIGN.md) | Decisiones de diseño, tradeoffs, zero-allocation, integración con StackExchange.Redis, mejoras futuras |

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Levantar el servidor

```bash
dotnet run --project src/KeyValueStore.Server
```

Opciones:

```bash
dotnet run --project src/KeyValueStore.Server -- --host 0.0.0.0 --port 6379

# O por variables de entorno
KV_HOST=0.0.0.0 KV_PORT=6379 dotnet run --project src/KeyValueStore.Server
```

## Probar con redis-cli

```bash
redis-cli -p 6379 PING
# → PONG
```

## Comandos soportados

### Strings

```bash
redis-cli -p 6379 SET foo bar          # +OK
redis-cli -p 6379 GET foo              # "bar"
redis-cli -p 6379 SET temp val EX 10   # Expira en 10 segundos
redis-cli -p 6379 TTL temp             # Segundos restantes
redis-cli -p 6379 INCR counter         # 1
redis-cli -p 6379 INCR counter         # 2
redis-cli -p 6379 DECR counter         # 1
```

### Keys

```bash
redis-cli -p 6379 DEL foo bar          # Cantidad de keys eliminadas
redis-cli -p 6379 EXISTS foo           # 1 o 0
redis-cli -p 6379 KEYS "user:*"        # Keys que matchean el patrón
redis-cli -p 6379 TYPE foo             # string, set, hash o none
redis-cli -p 6379 DBSIZE               # Cantidad de keys activas
redis-cli -p 6379 FLUSHALL             # Vaciar todo
```

### Sets

```bash
redis-cli -p 6379 SADD tags redis cache  # 2 (miembros agregados)
redis-cli -p 6379 SADD tags cache db     # 1 (solo db es nuevo)
redis-cli -p 6379 SMEMBERS tags          # redis, cache, db
redis-cli -p 6379 SISMEMBER tags redis   # 1
redis-cli -p 6379 SCARD tags             # 3
redis-cli -p 6379 SREM tags cache        # 1
```

### Hashes

```bash
redis-cli -p 6379 HSET user:1 name Alice    # 1
redis-cli -p 6379 HSET user:1 email a@b.com # 1
redis-cli -p 6379 HGET user:1 name          # "Alice"
redis-cli -p 6379 HGETALL user:1            # name, Alice, email, a@b.com
redis-cli -p 6379 HEXISTS user:1 email      # 1
redis-cli -p 6379 HLEN user:1               # 2
redis-cli -p 6379 HDEL user:1 email         # 1
```

### Pub/Sub

```bash
# Terminal 1 — Suscriptor
redis-cli -p 6379 SUBSCRIBE orders        # → subscribe, orders, :1
                                          # (espera mensajes)

# Terminal 2 — Publicador
redis-cli -p 6379 PUBLISH orders new!     # → :1 (entregado a 1 suscriptor)

# Terminal 1 recibe automáticamente:
# → "message", "orders", "new!"

# Por patrón glob
redis-cli -p 6379 PSUBSCRIBE orders.*     # recibe orders.created, orders.updated

# Cancelar suscripción
redis-cli UNSUBSCRIBE orders
redis-cli PUNSUBSCRIBE orders.*
```

## Probar sin redis-cli (telnet / netcat)

```bash
echo -e "PING\r\n" | nc 127.0.0.1 6379     # +PONG
echo -e "SET foo bar\r\nGET foo\r\n" | nc 127.0.0.1 6379  # +OK, $3, bar
```

## Tests

```bash
dotnet test
# 206 tests, 0 fallados
```

## Publicar

```bash
dotnet publish src/KeyValueStore.Server -c Release -o ./publish
```

El ejecutable queda en `./publish/KeyValueStore.Server`.

### Ejecutar la versión publicada

```bash
# Windows
set KV_HOST=0.0.0.0 & set KV_PORT=6380 & .\publish\KeyValueStore.Server.exe

# Linux / macOS
KV_HOST=0.0.0.0 KV_PORT=6380 ./publish/KeyValueStore.Server

# O con argumentos CLI (tienen precedencia sobre las variables)
./publish/KeyValueStore.Server --host 0.0.0.0 --port 6380
```
