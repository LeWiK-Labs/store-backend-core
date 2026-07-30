#!/usr/bin/env bash
# =============================================================================
# LeWiK Store API — E2E smoke test  (bash + curl + jq)
#
# Requisitos:  API corriendo en $API, Postgres+Redis arriba, y `jq` instalado.
#   docker compose --profile full up -d --build      # API + Postgres + Redis
#   # o, si la API corre desde el IDE:
#   docker compose up -d                             # solo Postgres + Redis
#
# Uso:
#   ./scripts/smoke-test.sh                # contra http://localhost:5223
#   API=http://host:puerto ./scripts/smoke-test.sh
#
# Idempotente: usa un sufijo único por corrida ($RUN) en emails y SKUs, así que
# no requiere base limpia. Sale con código != 0 si alguna aserción "dura" falla.
# Las ZONAS DE RIESGO conocidas se reportan como WARN (no rompen el build).
#
# ---- Desde 4.3: el script se autentica ----------------------------------------
# La gestión vive bajo /admin y exige sesión de staff + tenant match, así que ya no
# alcanza con inventar un GUID en el header: el tenant tiene que ser una TIENDA de
# verdad (staff_users tiene FK a stores). Por eso la sección 0 crea sus propias
# tiendas vía /platform/stores y se loguea como sus dueños. Cada corrida deja dos
# tiendas descartables; para limpiar la base de desarrollo:
#   DELETE FROM stores WHERE slug LIKE 'smoke-%';   -- staff y sesiones caen por FK
#
# Requiere un PlatformOperator ya sembrado (Platform:SeedOperator* en appsettings).
# Se puede apuntar a otro con OPERATOR_EMAIL / OPERATOR_PASSWORD.
#
# ---- Desde 4.4: el header de tenant es una comodidad de desarrollo -------------
# La tienda se resuelve por el dominio de la request. X-Tenant-Id sigue existiendo
# pero detrás de Tenancy:AllowHeaderOverride, y la app se niega a arrancar con eso
# prendido fuera de Development. Las secciones 1–17 lo siguen usando (es lo que hace
# el script legible); la sección 18 no lo manda nunca y prueba el camino real, que es
# el único que existe en producción.
#
# La sección 18 necesita Tenancy:BaseDomain = "localhost" (el default de
# appsettings.Development.json) y crea una tercera tienda con dominio propio.
#
# ---- Desde 4.6: la sección 20 mueve el reloj, no espera ------------------------
# Las reservas sin pagar vencen a los 30 minutos. Para probarlo sin esperar 30 minutos, la
# sección 20 corre el vencimiento hacia atrás con `docker exec lewik_store_db psql` y espera un
# ciclo del barredor (SweepIntervalSeconds, 10s en Development). Si Postgres no se alcanza por
# ahí, la sección se omite sola. Con EXPIRY_WAIT se ajusta la espera.
#
# ---- Desde 4.7: la sección 21 habla SignalR con curl ---------------------------
# El contador en vivo se prueba de punta a punta sobre el transporte de long polling, que es
# HTTP plano: negotiate, handshake, WatchVariant, y leer los frames que empuja el servidor. No
# hace falta navegador (para mirarlo con los ojos está /signalr-test.html, servido por la API en
# Development). Lo que exige ese camino es lo que un navegador no puede afirmar: que a la OTRA
# tienda no le llega nada.
# =============================================================================
set -u

API="${API:-http://localhost:5223}"
OPERATOR_EMAIL="${OPERATOR_EMAIL:-admin@lewik.cl}"
OPERATOR_PASSWORD="${OPERATOR_PASSWORD:-cambiar-esto-ya-1234}"
OWNER_PASSWORD="password-larga-123"
RUN="$(date +%s)"
BODY="$(mktemp)"
# Se llenan en la sección 0; declarados acá porque los helpers los usan por defecto.
TA=""; TB=""
JARP="$BODY.jar.platform"; JAR_A="$BODY.jar.a"; JAR_B="$BODY.jar.b"
# touch: `curl -b` interpreta un archivo inexistente como una cookie literal.
touch "$JARP" "$JAR_A" "$JAR_B"
trap 'rm -f "$BODY" "$BODY".*' EXIT

command -v jq >/dev/null 2>&1 || { echo "ERROR: jq no está instalado."; exit 2; }

# ---- colores / contadores ---------------------------------------------------
if [ -t 1 ]; then G=$'\e[32m'; R=$'\e[31m'; Y=$'\e[33m'; C=$'\e[36m'; Z=$'\e[0m'; else G=; R=; Y=; C=; Z=; fi
PASS=0; FAIL=0; WARN=0

ok()   { PASS=$((PASS+1)); printf "  ${G}PASS${Z} %-6s %s\n" "$1" "$2"; }
ko()   { FAIL=$((FAIL+1)); printf "  ${R}FAIL${Z} %-6s %s\n" "$1" "$2"; }
warn() { WARN=$((WARN+1)); printf "  ${Y}WARN${Z} %-6s %s\n" "$1" "$2"; }
section() { printf "\n${C}== %s ==${Z}\n" "$1"; }

# ---- helpers HTTP -----------------------------------------------------------
# Tres formas de llamar, porque ahora hay tres poblaciones distintas:
#   req  -> el panel: sesión de staff de la tienda A + header CSRF (lo más común)
#   creq -> igual pero con un tarro de cookies explícito (otra tienda, un cajero,
#           el operador de plataforma)
#   pub  -> sin credenciales, como el navegador de un comprador
# Que lo público tenga que pedirse con otro helper es a propósito: si una prueba
# de superficie pública pasa usando req(), está pasando por la sesión y no prueba
# nada.

# req METHOD PATH [json] [tenant] [jar]  -> status en stdout, body en $BODY
req() {
  # OJO: ${4-...} sin dos puntos, para que un tenant "" explícito quede vacío
  # (con :- un string vacío se sustituiría por $TA y rompería las pruebas "sin tenant").
  local method="$1" path="$2" json="${3:-}" tenant="${4-$TA}" jar="${5-$JAR_A}"
  local -a args=(-s -o "$BODY" -w '%{http_code}' -X "$method" "$API$path"
                 -H "X-Requested-With: LeWiKPanel")
  [ -n "$jar" ] && args+=(-b "$jar" -c "$jar")
  [ -n "$tenant" ] && args+=(-H "X-Tenant-Id: $tenant")
  [ -n "$json" ] && args+=(-H "Content-Type: application/json" -d "$json")
  curl "${args[@]}"
}

# creq METHOD PATH [json] [tenant] JAR
# (-b lee, -c escribe: hace falta leer Y escribir para que login/uso/logout compartan sesión)
creq() { req "$1" "$2" "${3:-}" "${4-}" "$5"; }

# plat METHOD PATH [json]  -> como operador de plataforma (nunca lleva tenant).
# Ignora argumentos de más, así que las llamadas que arrastran un "" de tenant
# desde antes de 4.3 siguen sirviendo tal cual.
plat() { creq "$1" "$2" "${3:-}" "" "$JARP"; }

# pub METHOD PATH [json] [tenant]  -> anónimo, sin cookie ni CSRF
pub() {
  local method="$1" path="$2" json="${3:-}" tenant="${4-$TA}"
  local -a args=(-s -o "$BODY" -w '%{http_code}' -X "$method" "$API$path")
  [ -n "$tenant" ] && args+=(-H "X-Tenant-Id: $tenant")
  [ -n "$json" ] && args+=(-H "Content-Type: application/json" -d "$json")
  curl "${args[@]}"
}

# dom METHOD PATH HOST [json] [jar]  -> como llega en producción (4.4): la tienda sale del
# dominio y NUNCA se manda X-Tenant-Id. El header de tenant es una comodidad de desarrollo
# apagada fuera de Development, así que todo lo que se pruebe con él prueba una ruta que en
# producción no existe; esto prueba la que sí.
#
# OJO con las cookies: curl las guarda y las manda por el HOST QUE MANDAMOS, no por el host
# de la URL. Un login con Host: panel.a.localhost deja la cookie atada a ese dominio y no
# viaja a panel.b.localhost — igual que en un navegador. Está bien que sea así, pero implica
# que un tarro no sirve para probar "la misma sesión contra otra tienda": para eso hay que
# repetir el token crudo a mano (ver 18.5c).
dom() {
  local method="$1" path="$2" h="$3" json="${4:-}" jar="${5-}"
  local -a args=(-s -o "$BODY" -w '%{http_code}' -X "$method" "$API$path"
                 -H "Host: $h" -H "X-Requested-With: LeWiKPanel")
  [ -n "$jar" ] && args+=(-b "$jar" -c "$jar")
  [ -n "$json" ] && args+=(-H "Content-Type: application/json" -d "$json")
  curl "${args[@]}"
}

jqr()  { jq -r "$1" "$BODY" 2>/dev/null; }
# jqnum: normaliza números (los montos vuelven como decimal "150000.0000"; "+0" canoniza a 150000)
jqnum() { jq -r "($1) + 0" "$BODY" 2>/dev/null; }

# assert_status ID DESC EXPECTED ACTUAL
assert_status() { if [ "$3" = "$4" ]; then ok "$1" "$2 (HTTP $4)"; else ko "$1" "$2 — esperado $3, obtenido $4 :: $(head -c160 "$BODY")"; fi; }
# assert_title ID DESC EXPECTED_STATUS EXPECTED_TITLE ACTUAL_STATUS
assert_title() { local t; t="$(jqr '.title')"; if [ "$3" = "$5" ] && [ "$4" = "$t" ]; then ok "$1" "$2 ($t)"; else ko "$1" "$2 — esperado $3/$4, obtenido $5/$t"; fi; }
# assert_eq ID DESC EXPECTED ACTUAL
assert_eq() { if [ "$3" = "$4" ]; then ok "$1" "$2"; else ko "$1" "$2 — esperado '$3' obtenido '$4'"; fi; }

# GUID v4 aleatorio, sin depender de uuidgen ni python.
# Sirve para pruebas que necesitan un tenant que NADIE configuró todavía: las
# credenciales de pasarela viven en la base y sobreviven entre corridas, así que
# afirmar "gateway sin configurar" sobre $TA depende de qué haya hecho antes el
# que corrió el script (o el que probó Webpay a mano).
guid() {
  local h; h="$(od -An -tx1 -N16 /dev/urandom | tr -d ' \n')"
  printf '%s-%s-4%s-a%s-%s\n' "${h:0:8}" "${h:8:4}" "${h:13:3}" "${h:17:3}" "${h:20:12}"
}

# ---- helpers SignalR (sección 21) -------------------------------------------
# El transporte de long polling es HTTP plano, así que el contador en vivo se prueba con curl y
# no queda como "lo vi andar en el navegador una vez". Y es la única forma de afirmar lo que de
# verdad importa acá, que es una ausencia: que a la otra tienda NO le llega nada.
#
# Secuencia del protocolo: negotiate -> primer GET (abre el transporte) -> POST del handshake ->
# GET del ack. Los frames van separados por 0x1E, que es lo que traduce el `tr` al leerlos.
# Imprime el connectionToken urlencodeado, o nada si la conexión fue rechazada.
hub_open() { # HOST
  local h="$1" tok id
  tok=$(curl -s -m 5 -X POST "$API/hubs/store/negotiate?negotiateVersion=1" -H "Host: $h" \
        | jq -r '.connectionToken // empty' 2>/dev/null)
  [ -z "$tok" ] && return 1
  id=$(jq -rn --arg t "$tok" '$t|@uri')
  curl -s -m 5 -o /dev/null "$API/hubs/store?id=$id" -H "Host: $h"
  printf '{"protocol":"json","version":1}\x1e' \
    | curl -s -m 5 -o /dev/null -X POST "$API/hubs/store?id=$id" -H "Host: $h" --data-binary @-
  curl -s -m 5 -o /dev/null "$API/hubs/store?id=$id" -H "Host: $h"
  printf '%s' "$id"
}

# hub_invoke ID HOST MÉTODO ARG -> imprime el frame de completion (type 3), con su .result.
# Vuelve recién cuando el hub confirmó la invocación.
# El invocationId no es decorativo: el POST de un frame devuelve 200 apenas el mensaje entra en
# la conexión, y el método del hub corre después, en el bucle de la conexión. Sin esperar el
# completion (type 3), un checkout puede ganarle a la suscripción y la prueba mediría una
# carrera en vez del comportamiento. Es también la razón de no usar sleeps acá.
hub_invoke() { # ID HOST TARGET ARG
  printf '{"type":1,"invocationId":"1","target":"%s","arguments":["%s"]}\x1e' "$3" "$4" \
    | curl -s -m 5 -o /dev/null -X POST "$API/hubs/store?id=$1" -H "Host: $2" --data-binary @-
  hub_poll "$1" "$2" 5 | grep '"type":3' | head -1
}

# hub_poll ID HOST SEGUNDOS -> los frames recibidos, uno por línea.
# Lo que ya se emitió mientras no había poll abierto queda encolado en la conexión y sale en el
# siguiente, así que no hace falta correr el poll en paralelo con la acción que lo dispara: se
# hace la acción y después se recoge. Si no hay nada, bloquea hasta el timeout y devuelve vacío,
# que es exactamente el resultado que se quiere medir del lado de la otra tienda.
hub_poll() { curl -s -m "$3" "$API/hubs/store?id=$1" -H "Host: $2" | tr '\036' '\n'; }

# =============================================================================
section "0 · Bootstrap: plataforma, tiendas y sesiones"
st=$(pub GET /health/db "" "")
assert_status 0.1 "GET /health/db" 200 "$st"
st=$(pub GET /health/cache "" ""); assert_eq 0.2 "cache ok (Redis)" "ok" "$(jqr '.cache')"

# El operador de plataforma es el único que puede crear tiendas.
st=$(creq POST /auth/platform/login \
  "{\"email\":\"$OPERATOR_EMAIL\",\"password\":\"$OPERATOR_PASSWORD\"}" "" "$JARP")
if [ "$st" != "200" ]; then
  ko 0.3 "login del operador de plataforma (HTTP $st) — sin esto no se puede correr nada"
  echo "  Sembrá uno con Platform:SeedOperatorEmail/Password, o pasá OPERATOR_EMAIL/OPERATOR_PASSWORD."
  exit 1
fi
ok 0.3 "login del operador de plataforma"

# Dos tiendas nuevas por corrida. Antes de 4.3 acá había dos GUID inventados; ya no
# sirven, porque el staff tiene FK a stores y sin tienda no hay con quién loguearse.
mkstore() { # slug-suffix -> imprime el id de la tienda; deja el owner en owner-<suffix>@smoke.cl
  creq POST /platform/stores \
    "{\"name\":\"Smoke $1\",\"slug\":\"smoke-$1-$RUN\",\"ownerEmail\":\"owner-$1-$RUN@smoke.cl\",\"ownerName\":\"Owner $1\",\"ownerPassword\":\"$OWNER_PASSWORD\"}" \
    "" "$JARP" >/dev/null
  jqr '.id'
}
# Tienda descartable CON sesión, para las pruebas que necesitan una tienda donde
# nadie configuró pasarelas todavía (las credenciales persisten entre corridas, así
# que afirmar "sin configurar" sobre $TA daba un rojo falso apenas alguien probaba
# Webpay de verdad). Imprime "<storeId> <jar>".
#
# El nombre lo pone quien llama, y no un contador: esto se invoca dentro de $( ),
# que es un subshell, así que cualquier contador que incrementara acá se perdería
# y la segunda tienda pediría el slug de la primera.
mkstore_session() { # nombre -> "<storeId> <jar>"
  local sid jar="$BODY.jar.$1"
  sid="$(mkstore "$1")"
  touch "$jar"
  creq POST /auth/staff/login \
    "{\"email\":\"owner-$1-$RUN@smoke.cl\",\"password\":\"$OWNER_PASSWORD\"}" "$sid" "$jar" >/dev/null
  printf '%s %s\n' "$sid" "$jar"
}

TA="$(mkstore a)"; TB="$(mkstore b)"
[ -n "$TA" ] && [ "$TA" != "null" ] && ok 0.4a "tienda A creada" || { ko 0.4a "no se pudo crear la tienda A"; exit 1; }
[ -n "$TB" ] && [ "$TB" != "null" ] && ok 0.4b "tienda B creada" || { ko 0.4b "no se pudo crear la tienda B"; exit 1; }

st=$(creq POST /auth/staff/login "{\"email\":\"owner-a-$RUN@smoke.cl\",\"password\":\"$OWNER_PASSWORD\"}" "$TA" "$JAR_A")
assert_status 0.5a "login del dueño de A" 200 "$st"
st=$(creq POST /auth/staff/login "{\"email\":\"owner-b-$RUN@smoke.cl\",\"password\":\"$OWNER_PASSWORD\"}" "$TB" "$JAR_B")
assert_status 0.5b "login del dueño de B" 200 "$st"

st=$(pub GET /health/tenant "" "$TA"); assert_eq 0.6 "tenant resuelto" "$TA" "$(jqr '.tenantId')"
st=$(pub GET /health/tenant "" ""); assert_eq 0.7 "sin tenant" "no tenant resolved" "$(jqr '.message')"
st=$(req POST "/hubs/store/negotiate?negotiateVersion=1" "{}" "$TA")
[ "$(jqr '.connectionId')" != "null" ] && ok 0.8 "SignalR negotiate" || ko 0.8 "SignalR negotiate (HTTP $st)"

section "1 · Catálogo: producto simple"
SKU="BOX-SV01-$RUN"
st=$(req POST /admin/products "{\"sku\":\"$SKU\",\"name\":\"SV Booster Box\",\"description\":\"36 sobres\",\"price\":150000,\"currency\":\"CLP\"}")
PROD_SIMPLE="$(jqr '.')"; assert_status 1.1 "crear producto simple" 200 "$st"
st=$(req GET "/products/$PROD_SIMPLE")
VAR_SIMPLE="$(jqr '.variants[0].id')"
assert_eq 1.2a "options vacío" "0" "$(jqr '.options | length')"
assert_eq 1.2b "variante Default" "Default" "$(jqr '.variants[0].label')"
assert_eq 1.2c "priceAmount" "150000" "$(jqnum '.variants[0].priceAmount')"
st=$(req POST /admin/products "{\"sku\":\"$SKU\",\"name\":\"dup\",\"price\":1,\"currency\":\"CLP\"}")
assert_title 1.3 "sku duplicado" 409 "catalog.duplicate_sku" "$st"
st=$(req POST /admin/products "{\"sku\":\"NODESC-$RUN\",\"name\":\"x\",\"price\":1000,\"currency\":\"CLP\"}")
assert_status 1.4 "sin description (opcional)" 200 "$st"
st=$(req POST /admin/products "{\"sku\":\"EMPTY-$RUN\",\"name\":\"\",\"price\":1000,\"currency\":\"CLP\"}")
assert_status 1.5 "name vacío" 400 "$st"
st=$(req POST /admin/products "{\"sku\":\"NEG-$RUN\",\"name\":\"n\",\"price\":-1,\"currency\":\"CLP\"}")
assert_status 1.6 "price -1" 400 "$st"
st=$(req POST /admin/products "{\"sku\":\"CUR-$RUN\",\"name\":\"c\",\"price\":1,\"currency\":\"CL\"}")
assert_status 1.7 "currency inválida" 400 "$st"
st=$(req GET "/products/99999999-9999-9999-9999-999999999999")
assert_title 1.8 "producto inexistente" 404 "catalog.product_not_found" "$st"

section "2 · Catálogo: producto con options (matriz)"
# NOTA: usamos valores ASCII (Ingles/Espanol) a propósito. El cuerpo debe ir en UTF-8;
# si el shell/locale envía acentos como Latin-1, la API responde 500 (ver informe).
st=$(req POST /admin/products/with-options "{
  \"name\":\"Blister Paldea Evolved\",
  \"options\":[{\"name\":\"Carta\",\"values\":[\"Charizard\",\"Meganium\"]},{\"name\":\"Idioma\",\"values\":[\"Ingles\",\"Espanol\"]}],
  \"variants\":[
    {\"sku\":\"BLI-CHAR-EN-$RUN\",\"price\":12990,\"currency\":\"CLP\",\"selections\":{\"Carta\":\"Charizard\",\"Idioma\":\"Ingles\"}},
    {\"sku\":\"BLI-CHAR-ES-$RUN\",\"price\":12990,\"currency\":\"CLP\",\"selections\":{\"Carta\":\"Charizard\",\"Idioma\":\"Espanol\"}},
    {\"sku\":\"BLI-MEGA-EN-$RUN\",\"price\":11990,\"currency\":\"CLP\",\"selections\":{\"Carta\":\"Meganium\",\"Idioma\":\"Ingles\"}}]}")
PROD_BLISTER="$(jqr '.')"; assert_status 2.1 "crear con opciones" 200 "$st"
st=$(req GET "/products/$PROD_BLISTER")
cp "$BODY" "$BODY.blister"
assert_eq 2.2a "2 ejes" "2" "$(jqr '.options | length')"
assert_eq 2.2b "eje 0 = Carta" "Carta" "$(jqr '.options[0].name')"
assert_eq 2.2c "3 variantes" "3" "$(jqr '.variants | length')"
# 2.4 mapeo de selectores: los optionValueIds de "Charizard / Ingles" == ids de Charizard e Ingles
CHAR_ID=$(jq -r '.options[] | select(.name=="Carta").values[] | select(.value=="Charizard").id' "$BODY.blister")
EN_ID=$(jq -r '.options[] | select(.name=="Idioma").values[] | select(.value=="Ingles").id' "$BODY.blister")
MAP=$(jq -r --arg a "$CHAR_ID" --arg b "$EN_ID" \
  '.variants[] | select(.label=="Charizard / Ingles") | (.optionValueIds | sort) == ([$a,$b] | sort)' "$BODY.blister")
assert_eq 2.4 "optionValueIds resuelven a Charizard+Ingles" "true" "$MAP"
VAR_DROP=$(jq -r '.variants[] | select(.label=="Meganium / Ingles").id' "$BODY.blister")
rm -f "$BODY.blister"
st=$(req POST /admin/products/with-options "{\"name\":\"b\",\"options\":[{\"name\":\"C\",\"values\":[\"A\",\"B\"]},{\"name\":\"I\",\"values\":[\"E\",\"S\"]}],\"variants\":[{\"sku\":\"B1-$RUN\",\"price\":1,\"currency\":\"CLP\",\"selections\":{\"C\":\"A\"}}]}")
assert_title 2.5 "selección incompleta" 400 "catalog.incomplete_selection" "$st"
st=$(req POST /admin/products/with-options "{\"name\":\"b\",\"options\":[{\"name\":\"C\",\"values\":[\"A\",\"B\"]}],\"variants\":[{\"sku\":\"B2-$RUN\",\"price\":1,\"currency\":\"CLP\",\"selections\":{\"C\":\"Z\"}}]}")
assert_title 2.6 "valor desconocido" 400 "catalog.unknown_selection" "$st"
st=$(req POST /admin/products/with-options "{\"name\":\"b\",\"options\":[{\"name\":\"C\",\"values\":[\"A\",\"B\"]}],\"variants\":[{\"sku\":\"B3a-$RUN\",\"price\":1,\"currency\":\"CLP\",\"selections\":{\"C\":\"A\"}},{\"sku\":\"B3b-$RUN\",\"price\":1,\"currency\":\"CLP\",\"selections\":{\"C\":\"A\"}}]}")
assert_title 2.7 "combinación duplicada" 409 "catalog.duplicate_combination" "$st"
st=$(req POST /admin/products/with-options "{\"name\":\"b\",\"options\":[{\"name\":\"C\",\"values\":[\"A\",\"A\"]}],\"variants\":[{\"sku\":\"B4-$RUN\",\"price\":1,\"currency\":\"CLP\",\"selections\":{\"C\":\"A\"}}]}")
assert_title 2.8 "valores de opción duplicados" 400 "catalog.duplicate_option_value" "$st"
st=$(req POST /admin/products/with-options "{\"name\":\"b\",\"options\":[{\"name\":\"C\",\"values\":[\"A\"]}],\"variants\":[]}")
assert_status 2.9 "variants vacío" 400 "$st"

section "3 · Aislamiento multi-tenant"
st=$(pub GET /products "" "$TB")
LEAK=$(jq -r --arg p "$PROD_SIMPLE" 'any(.[]; .id==$p)' "$BODY")
assert_eq 3.1 "TB no ve productos de TA" "false" "$LEAK"
st=$(creq POST /admin/products "{\"sku\":\"$SKU\",\"name\":\"tb\",\"price\":1,\"currency\":\"CLP\"}" "$TB" "$JAR_B")
assert_status 3.2 "sku único por tenant" 200 "$st"
st=$(pub GET "/products/$PROD_SIMPLE" "" "$TB"); assert_status 3.3 "TB no ve producto de A" 404 "$st"
st=$(pub GET "/variants/$VAR_SIMPLE/stock" "" "$TB"); assert_status 3.4 "TB no ve stock de A" 404 "$st"
# Antes daba 400 "sin tenant": el guard del pipeline era lo único que miraba. Ahora
# la política corta antes, y sin tenant resuelto el TenantMatch no puede pasar.
st=$(req POST /admin/products "{\"sku\":\"NT-$RUN\",\"name\":\"x\",\"price\":1,\"currency\":\"CLP\"}" "")
assert_status 3.5 "POST sin tenant" 403 "$st"
st=$(pub GET /products "" ""); assert_eq 3.6 "GET sin tenant = lista vacía" "0" "$(jqr 'length')"
# La sesión de A no opera la tienda B, aunque el header diga B.
st=$(req POST /admin/products "{\"sku\":\"XT-$RUN\",\"name\":\"x\",\"price\":1,\"currency\":\"CLP\"}" "$TB")
assert_status 3.7 "sesión de A contra tienda B" 403 "$st"

section "4 · Inventario"
st=$(req POST "/admin/variants/$VAR_SIMPLE/stock" '{"quantity":10,"reason":"initial restock"}')
assert_eq 4.1 "stock inicial 10" "10" "$(jqr '.available')"
st=$(req POST "/admin/variants/$VAR_SIMPLE/stock" '{"quantity":5,"reason":"more"}')
assert_eq 4.2 "acumula a 15" "15" "$(jqr '.available')"
st=$(req GET "/variants/$VAR_SIMPLE/stock"); assert_eq 4.3 "GET stock 15" "15" "$(jqr '.available')"
st=$(req GET "/variants/88888888-8888-8888-8888-888888888888/stock")
assert_title 4.4 "sin inventario" 404 "inventory.not_found" "$st"
st=$(req POST "/admin/variants/$VAR_SIMPLE/stock" '{"quantity":0}'); assert_status 4.5 "quantity 0" 400 "$st"
st=$(req POST "/admin/variants/$VAR_SIMPLE/stock" '{"quantity":-5}'); assert_status 4.6 "quantity -5" 400 "$st"
st=$(req POST "/admin/variants/77777777-7777-7777-7777-777777777777/stock" '{"quantity":3}')
assert_title 4.7 "AddStock a variante inexistente rechazado" 404 "inventory.variant_not_found" "$st"

section "5 · Preventas / drops"
DROP='{"capacity":100,"releaseDate":"2026-09-01T00:00:00Z","depositType":"Percentage","depositValue":30}'
st=$(req PUT "/admin/variants/$VAR_DROP/preorder" "$DROP")
assert_eq 5.1a "capacity 100" "100" "$(jqr '.capacity')"
assert_eq 5.1b "status Active" "Active" "$(jqr '.status')"
st=$(req PUT "/admin/variants/$VAR_DROP/preorder" '{"capacity":200,"releaseDate":"2026-09-01T00:00:00Z","depositType":"Percentage","depositValue":30}')
assert_eq 5.2 "upsert capacity 200" "200" "$(jqr '.capacity')"
st=$(req GET "/variants/$VAR_DROP/preorder"); assert_status 5.3 "GET preorder" 200 "$st"
st=$(req GET "/variants/$VAR_SIMPLE/preorder"); assert_title 5.4 "sin drop" 404 "preorder.not_found" "$st"
st=$(req PUT "/admin/variants/$VAR_DROP/preorder" '{"capacity":100,"releaseDate":"2026-09-01T00:00:00Z","depositType":"Percentage","depositValue":150}')
assert_status 5.5 "porcentaje 150 fuera de rango" 400 "$st"
st=$(req PUT "/admin/variants/$VAR_DROP/preorder" '{"capacity":100,"releaseDate":"2026-09-01T00:00:00Z","depositType":"FixedPerUnit","depositValue":5000}')
assert_status 5.6 "FixedPerUnit 5000" 200 "$st"
st=$(req PUT "/admin/variants/$VAR_DROP/preorder" '{"capacity":0,"releaseDate":"2026-09-01T00:00:00Z","depositType":"Percentage","depositValue":30}')
assert_status 5.8 "capacity 0" 400 "$st"
st=$(req PUT "/admin/variants/$VAR_DROP/preorder" '{"capacity":100,"releaseDate":"2026-09-01T00:00:00Z","depositType":"Porcentaje","depositValue":30}')
assert_title 5.9 "enum inválido -> 400 (F3)" 400 "request.invalid_body" "$st"
# dejar configurado Percentage/30 capacity 100 para el escenario 10
req PUT "/admin/variants/$VAR_DROP/preorder" "$DROP" >/dev/null

section "6 · Límites anti-scalping (config)"
st=$(req PUT "/admin/products/$PROD_SIMPLE/purchase-limit" '{"maxPerOrder":5,"maxPerCustomer":5,"windowDays":30}')
assert_eq 6.1 "scope Product" "Product" "$(jqr '.scope')"
st=$(req PUT "/admin/variants/$VAR_SIMPLE/purchase-limit" '{"maxPerOrder":2,"maxPerCustomer":3,"windowDays":30}')
assert_eq 6.2 "scope Variant" "Variant" "$(jqr '.scope')"
st=$(req PUT "/admin/products/$PROD_SIMPLE/purchase-limit" '{"windowDays":30}')
assert_status 6.4 "sin ningún máximo" 400 "$st"
st=$(req PUT "/admin/products/$PROD_SIMPLE/purchase-limit" '{"maxPerOrder":0}')
assert_status 6.5 "maxPerOrder 0" 400 "$st"
# probe aparte para no ensuciar el límite real de PROD_SIMPLE
st=$(req POST /admin/products "{\"sku\":\"LIM-$RUN\",\"name\":\"probe\",\"price\":100,\"currency\":\"CLP\"}"); PROBE="$(jqr '.')"
st=$(req PUT "/admin/products/$PROBE/purchase-limit" '{"maxPerOrder":2}')
assert_eq 6.6 "solo maxPerOrder -> windowDays null" "null" "$(jqr '.windowDays')"
st=$(req PUT "/admin/products/$PROBE/purchase-limit" '{"maxPerCustomer":null,"windowDays":30,"maxPerOrder":4}')
assert_eq 6.6b "sin cap por cliente anula ventana" "null" "$(jqr '.windowDays')"
st=$(req GET "/admin/products/$PROD_BLISTER/purchase-limit")
assert_title 6.7 "producto sin política" 404 "catalog.no_purchase_limit" "$st"
# restaurar límite conocido de PROD_SIMPLE
req PUT "/admin/products/$PROD_SIMPLE/purchase-limit" '{"maxPerOrder":5,"maxPerCustomer":5,"windowDays":30}' >/dev/null

# helper de checkout
checkout() { # email variant qty  -> status; body en $BODY
  pub POST /orders "{\"customer\":{\"email\":\"$1\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$2\",\"quantity\":$3}]}"
}

section "7 · Checkout desde stock"
st=$(checkout "cliente-$RUN@test.cl" "$VAR_SIMPLE" 2); ORDER="$(jqr '.orderId')"
assert_status 7.1 "checkout qty 2" 200 "$st"
st=$(req GET "/admin/orders/$ORDER"); LINE="$(jqr '.lines[0].id')"
assert_eq 7.2a "fulfillmentStatus" "PendingPayment" "$(jqr '.fulfillmentStatus')"
assert_eq 7.2b "total 300000" "300000" "$(jqnum '.total')"
assert_eq 7.2c "depositDue == total (línea stock)" "300000" "$(jqnum '.depositDue')"
assert_eq 7.3 "isPreorder false" "false" "$(jqr '.lines[0].isPreorder')"
st=$(req GET "/variants/$VAR_SIMPLE/stock")
assert_eq 7.4a "reservado 2" "2" "$(jqr '.reserved')"
assert_eq 7.4b "disponible 13" "13" "$(jqr '.available')"
st=$(checkout "nov-$RUN@test.cl" "66666666-6666-6666-6666-666666666666" 1)
assert_title 7.6 "variante inexistente" 404 "order.variant_not_found" "$st"
st=$(pub POST /orders "{\"customer\":{\"email\":\"empty-$RUN@test.cl\",\"phone\":\"+56911111111\"},\"items\":[]}")
assert_status 7.8 "items vacío" 400 "$st"
st=$(pub POST /orders "{\"customer\":{\"email\":\"bad\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$VAR_SIMPLE\",\"quantity\":1}]}")
assert_status 7.9 "email inválido" 400 "$st"
# 7.10 merge de items duplicados (email fresco para no chocar con el límite por cliente)
st=$(pub POST /orders "{\"customer\":{\"email\":\"merge-$RUN@test.cl\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$VAR_SIMPLE\",\"quantity\":1},{\"productVariantId\":\"$VAR_SIMPLE\",\"quantity\":1}]}")
MID="$(jqr '.orderId')"; req GET "/admin/orders/$MID" >/dev/null
assert_eq 7.10 "items duplicados se fusionan" "2" "$(jqr '.lines[0].qtyOrdered')"
# 7.11 reusar email -> mismo customer
st=$(checkout "cliente-$RUN@test.cl" "$VAR_SIMPLE" 1); O2="$(jqr '.orderId')"
req GET "/admin/orders/$ORDER"  >/dev/null; C1="$(jqr '.customerId')"
req GET "/admin/orders/$O2"     >/dev/null; C2="$(jqr '.customerId')"
assert_eq 7.11 "mismo email -> mismo customerId" "$C1" "$C2"

section "8 · Enforcement anti-scalping"
SC="scalper-$RUN@test.cl"
st=$(checkout "$SC" "$VAR_SIMPLE" 3); assert_title 8.1 "3 > maxPerOrder 2" 409 "order.limit_per_order" "$st"
st=$(checkout "$SC" "$VAR_SIMPLE" 2); SCO="$(jqr '.orderId')"; assert_status 8.2 "2 dentro del límite" 200 "$st"
st=$(checkout "$SC" "$VAR_SIMPLE" 2); assert_title 8.3 "2+2>3 por cliente" 409 "order.limit_per_customer" "$st"
st=$(checkout "$SC" "$VAR_SIMPLE" 1); assert_status 8.4 "2+1=3 justo" 200 "$st"
st=$(checkout "$SC" "$VAR_SIMPLE" 1); assert_title 8.5 "3+1>3" 409 "order.limit_per_customer" "$st"
st=$(checkout "otro-$RUN@test.cl" "$VAR_SIMPLE" 1); assert_status 8.6 "otro cliente ok" 200 "$st"
req POST "/admin/orders/$SCO/cancel" >/dev/null
st=$(checkout "$SC" "$VAR_SIMPLE" 2); assert_status 8.7 "cancelados no cuentan en historial" 200 "$st"

section "9 · Ciclo de vida del pedido (stock)"
st=$(req POST "/admin/orders/$ORDER/prepare"); assert_title 9.1 "prepare antes de pagar" 409 "order.invalid_transition" "$st"
st=$(req POST "/admin/orders/$ORDER/payments" '{"amount":500000}'); assert_title 9.2 "pago > balance" 409 "order.payment_exceeds_balance" "$st"
st=$(req POST "/admin/orders/$ORDER/payments" '{"amount":100000}')
assert_eq 9.3a "pago parcial -> Deposited" "Deposited" "$(jqr '.paymentStatus')"
assert_eq 9.3b "balance 200000" "200000" "$(jqnum '.balance')"
st=$(req POST "/admin/orders/$ORDER/payments" '{"amount":200000}')
assert_eq 9.4 "pago total -> Paid" "Paid" "$(jqr '.paymentStatus')"
# 9.5 ZONA DE RIESGO 2: en pedido SOLO-stock, el pago parcial ya lo empujó a AwaitingRelease
# F1: pedido solo-stock queda 'Paid' tras pago total (nunca en AwaitingRelease)
req GET "/admin/orders/$ORDER" >/dev/null
assert_eq 9.5 "solo-stock -> Paid tras pago total (F1)" "Paid" "$(jqr '.fulfillmentStatus')"
st=$(req POST "/admin/orders/$ORDER/payments" '{"amount":1}'); assert_title 9.6 "pagar de nuevo" 409 "order.already_paid" "$st"
st=$(req POST "/admin/orders/$ORDER/prepare"); assert_eq 9.7 "prepare -> Preparing (F1)" "Preparing" "$(jqr '.fulfillmentStatus')"
st=$(req POST "/admin/orders/$ORDER/lines/$LINE/fulfill" '{"quantity":1}')
assert_eq 9.8 "entrega parcial -> PartiallyDelivered" "PartiallyDelivered" "$(jqr '.fulfillmentStatus')"
st=$(req POST "/admin/orders/$ORDER/lines/$LINE/fulfill" '{"quantity":5}')
assert_title 9.10 "fulfill excede pendiente" 409 "order.fulfill_exceeds_pending" "$st"
st=$(req POST "/admin/orders/$ORDER/lines/$LINE/fulfill" '{"quantity":1}')
assert_eq 9.11 "entrega total -> Delivered" "Delivered" "$(jqr '.fulfillmentStatus')"
st=$(req POST "/admin/orders/$ORDER/cancel"); assert_title 9.13 "cancelar entregado" 409 "order.cannot_cancel_delivered" "$st"
st=$(req POST "/admin/orders/$ORDER/lines/12345678-1234-1234-1234-123456789abc/fulfill" '{"quantity":1}')
assert_title 9.14 "línea inexistente" 404 "order.line_not_found" "$st"

section "10 · Preventa con abono (drop)"
st=$(pub POST /orders "{\"customer\":{\"email\":\"drop-$RUN@test.cl\",\"phone\":\"+56922222222\"},\"items\":[{\"productVariantId\":\"$VAR_DROP\",\"quantity\":2}]}")
ORDER_DROP="$(jqr '.orderId')"; assert_status 10.1 "checkout preventa qty 2" 200 "$st"
st=$(req GET "/admin/orders/$ORDER_DROP"); DLINE="$(jqr '.lines[0].id')"
assert_eq 10.2a "total 23980" "23980" "$(jqnum '.total')"
assert_eq 10.2b "depositDue 7194 (30%)" "7194" "$(jqnum '.depositDue')"
assert_eq 10.2c "isPreorder true" "true" "$(jqr '.lines[0].isPreorder')"
st=$(req GET "/variants/$VAR_DROP/preorder"); assert_eq 10.3 "soldCount 2" "2" "$(jqr '.soldCount')"
st=$(req GET "/variants/$VAR_DROP/stock"); assert_status 10.4 "sin inventario (venta contra cupo)" 404 "$st"
st=$(req POST "/admin/orders/$ORDER_DROP/payments" '{"amount":7194}')
assert_eq 10.5a "abono -> Deposited" "Deposited" "$(jqr '.paymentStatus')"
assert_eq 10.5b "-> AwaitingRelease" "AwaitingRelease" "$(jqr '.fulfillmentStatus')"
st=$(req POST "/admin/orders/$ORDER_DROP/release"); assert_title 10.6 "release con saldo" 409 "order.balance_pending" "$st"
st=$(req POST "/admin/orders/$ORDER_DROP/payments" '{"amount":16786}'); assert_eq 10.7 "saldo -> Paid" "Paid" "$(jqr '.paymentStatus')"
# F2: release exige que el stock del drop ya haya llegado (conversión cupo -> stock físico)
st=$(req POST "/admin/orders/$ORDER_DROP/release"); assert_title 10.8a "release sin restock" 409 "order.preorder_stock_missing" "$st"
req POST "/admin/variants/$VAR_DROP/stock" '{"quantity":2,"reason":"drop arrived"}' >/dev/null
st=$(req POST "/admin/orders/$ORDER_DROP/release"); assert_eq 10.8b "release tras restock -> Paid" "Paid" "$(jqr '.fulfillmentStatus')"
req GET "/variants/$VAR_DROP/stock" >/dev/null; assert_eq 10.8c "release reserva el stock del drop" "2" "$(jqr '.reserved')"
st=$(req POST "/admin/orders/$ORDER_DROP/prepare"); assert_eq 10.9 "prepare -> Preparing" "Preparing" "$(jqr '.fulfillmentStatus')"
# 10.10 (post-F2): fulfill de preventa consume la reserva real; soldCount NO cambia (cupo consumido)
st=$(req POST "/admin/orders/$ORDER_DROP/lines/$DLINE/fulfill" '{"quantity":2}')
assert_eq 10.10a "fulfill preventa -> Delivered" "Delivered" "$(jqr '.fulfillmentStatus')"
req GET "/variants/$VAR_DROP/stock" >/dev/null
assert_eq 10.10b "stock consumido (reserved 0)" "0" "$(jqr '.reserved')"
req GET "/variants/$VAR_DROP/preorder" >/dev/null
assert_eq 10.10c "soldCount SIN cambios (cupo consumido)" "2" "$(jqr '.soldCount')"
st=$(pub POST /orders "{\"customer\":{\"email\":\"dropover-$RUN@test.cl\",\"phone\":\"+56922222222\"},\"items\":[{\"productVariantId\":\"$VAR_DROP\",\"quantity\":100000}]}")
assert_title 10.11 "excede cupo" 409 "preorder.capacity_exceeded" "$st"

section "11 · Cancelación y liberación de reservas"
req GET "/variants/$VAR_SIMPLE/stock" >/dev/null; A0="$(jqr '.available')"; R0="$(jqr '.reserved')"
st=$(checkout "cancel-$RUN@test.cl" "$VAR_SIMPLE" 2); CO="$(jqr '.orderId')"
req GET "/variants/$VAR_SIMPLE/stock" >/dev/null
assert_eq 11.1 "reserva +2" "$((R0+2))" "$(jqr '.reserved')"
st=$(req POST "/admin/orders/$CO/cancel"); assert_eq 11.2 "cancel -> Cancelled" "Cancelled" "$(jqr '.fulfillmentStatus')"
req GET "/variants/$VAR_SIMPLE/stock" >/dev/null
assert_eq 11.3a "available restaurado" "$A0" "$(jqr '.available')"
assert_eq 11.3b "reserved restaurado" "$R0" "$(jqr '.reserved')"
st=$(req POST "/admin/orders/$CO/cancel"); assert_title 11.4 "cancelar de nuevo" 409 "order.cancelled" "$st"
st=$(req POST "/admin/orders/$CO/payments" '{"amount":1000}'); assert_title 11.5 "pagar cancelado" 409 "order.cancelled" "$st"
# 11.6 cancelar preventa restaura soldCount
req GET "/variants/$VAR_DROP/preorder" >/dev/null; SD0="$(jqr '.soldCount')"
st=$(pub POST /orders "{\"customer\":{\"email\":\"cancelpre-$RUN@test.cl\",\"phone\":\"+56922222222\"},\"items\":[{\"productVariantId\":\"$VAR_DROP\",\"quantity\":3}]}"); PO="$(jqr '.orderId')"
req POST "/admin/orders/$PO/cancel" >/dev/null
req GET "/variants/$VAR_DROP/preorder" >/dev/null
assert_eq 11.6 "soldCount vuelve a $SD0" "$SD0" "$(jqr '.soldCount')"

section "12 · Pagos por transferencia"
st=$(req PUT /admin/payment-methods/Transfer '{"credentialsJson":"{\"banco\":\"Banco Estado\",\"numero\":\"123456789\",\"titular\":\"TCG Store SpA\"}"}')
assert_eq 12.1a "gateway Transfer activo" "Transfer" "$(jqr '.gateway')"
assert_eq 12.1b "sin devolver credenciales" "null" "$(jqr '.credentialsJson')"
st=$(checkout "transfer-$RUN@test.cl" "$VAR_SIMPLE" 1); TO="$(jqr '.orderId')"
req GET "/admin/orders/$TO" >/dev/null; BAL="$(jqr '.balance')"
st=$(pub POST "/orders/$TO/payments/initiate" '{"gateway":"Transfer","type":"Full"}')
PID="$(jqr '.paymentId')"
assert_eq 12.2a "amount == balance" "$BAL" "$(jqr '.amount')"
assert_eq 12.2b "redirectUrl null" "null" "$(jqr '.redirectUrl')"
[ "$(jqr '.bankDetails')" != "null" ] && ok 12.2c "bankDetails presente" || ko 12.2c "bankDetails ausente"
st=$(req POST "/admin/payments/$PID/confirm" '{"externalReference":"TRF-001"}')
assert_eq 12.3a "paymentState Succeeded" "Succeeded" "$(jqr '.paymentState')"
assert_eq 12.3b "orderPaymentStatus Paid" "Paid" "$(jqr '.orderPaymentStatus')"
assert_eq 12.3c "orderBalance 0" "0" "$(jqnum '.orderBalance')"
st=$(req POST "/admin/payments/$PID/confirm" '{"externalReference":"TRF-001"}')
assert_title 12.4 "idempotencia (doble confirm)" 409 "payment.already_resolved" "$st"
req GET "/admin/orders/$TO" >/dev/null; assert_eq 12.5 "balance sin cambios" "0" "$(jqnum '.balance')"
# Tienda efímera: recién creada no tiene ninguna pasarela configurada, así que la
# aserción vale sin importar qué haya en la base.
read -r TC JAR_C <<< "$(mkstore_session nogw)"
creq POST /admin/products "{\"sku\":\"NOGW-$RUN\",\"name\":\"Sin pasarelas\",\"price\":9000,\"currency\":\"CLP\"}" "$TC" "$JAR_C" >/dev/null
PROD_NOGW="$(jqr '.')"
pub GET "/products/$PROD_NOGW" "" "$TC" >/dev/null; VAR_NOGW="$(jqr '.variants[0].id')"
creq POST "/admin/variants/$VAR_NOGW/stock" '{"quantity":1}' "$TC" "$JAR_C" >/dev/null
pub POST /orders "{\"customer\":{\"email\":\"nogw-$RUN@test.cl\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$VAR_NOGW\",\"quantity\":1}]}" "$TC" >/dev/null
WO="$(jqr '.orderId')"
st=$(pub POST "/orders/$WO/payments/initiate" '{"gateway":"Webpay","type":"Full"}' "$TC")
assert_title 12.6a "Webpay sin configurar" 409 "payment.gateway_not_configured" "$st"
st=$(pub POST "/orders/$WO/payments/initiate" '{"gateway":"MercadoPago","type":"Full"}' "$TC")
assert_title 12.6b "MercadoPago sin configurar" 409 "payment.gateway_not_configured" "$st"
st=$(pub POST "/orders/$TO/payments/initiate" '{"gateway":"Transfer","type":"Full"}')
assert_title 12.7 "initiate sobre pagado" 409 "order.already_paid" "$st"
st=$(req POST "/admin/payments/00000000-0000-0000-0000-000000000001/confirm" '{"externalReference":"x"}')
assert_title 12.8 "payment inexistente" 404 "payment.not_found" "$st"

# =============================================================================
# Transferencia es la única pasarela que permite ejercer el mecanismo completo de
# reembolso sin depender de nadie: no hay API que llamar, la tienda devuelve la
# plata a mano y esto solo lo registra. Todo lo que se afirma acá (topes, tope
# parcial, idempotencia, que PaymentStatus NO retroceda) es común a las tres.
# $TO es el pedido de la sección 12: 1 × 150000, pagado entero con $PID.
section "12c · Reembolsos"
st=$(req GET "/admin/orders/$TO/payments")
assert_status 12c.1a "listar cargos del pedido" 200 "$st"
assert_eq 12c.1b "un cargo" "1" "$(jqr '. | length')"
assert_eq 12c.1c "cargo Succeeded" "Succeeded" "$(jqr '.[0].state')"
assert_eq 12c.1d "sin reembolsos todavía" "0" "$(jqnum '.[0].refundedAmount')"
assert_eq 12c.1e "es el pago de la sección 12" "$PID" "$(jqr '.[0].id')"

# Los rechazos van ANTES del primer reembolso: con la pasarela real, cada uno de
# estos que se colara sería plata saliendo de la cuenta de la tienda.
st=$(req POST "/admin/payments/$PID/refund" '{"amount":999999999}')
assert_title 12c.2a "monto mayor al cargo" 409 "refund.exceeds_payment" "$st"
st=$(req POST "/admin/payments/$PID/refund" '{"amount":0}')
assert_status 12c.2b "monto 0" 400 "$st"
st=$(req POST "/admin/payments/$PID/refund" '{"amount":-5000}')
assert_status 12c.2c "monto negativo" 400 "$st"
st=$(req POST "/admin/payments/00000000-0000-0000-0000-000000000001/refund" '{}')
assert_title 12c.2d "payment inexistente" 404 "payment.not_found" "$st"
# Aislamiento: el cargo es de $TA. Desde 4.3 ni siquiera se llega al handler —
# la política corta antes por tenant mismatch, así que es 403 y no el 404 que
# devolvía el filtro de consultas. Mejor: la sesión es buena, la tienda no.
st=$(req POST "/admin/payments/$PID/refund" '{}' "$TB")
assert_status 12c.2e "cargo de otro tenant" 403 "$st"
st=$(req GET "/admin/orders/$TO/payments" "" "$TB")
assert_status 12c.2f "cargos de otro tenant" 403 "$st"
# Un cargo sin confirmar no tiene plata que devolver.
st=$(checkout "refund-pending-$RUN@test.cl" "$VAR_SIMPLE" 1); RPO="$(jqr '.orderId')"
pub POST "/orders/$RPO/payments/initiate" '{"gateway":"Transfer","type":"Full"}' >/dev/null
RPP="$(jqr '.paymentId')"
st=$(req POST "/admin/payments/$RPP/refund" '{}')
assert_title 12c.2g "cargo Pending no es reembolsable" 409 "refund.payment_not_refundable" "$st"

# reason en ASCII a propósito, igual que en la sección 2: Git Bash pasa los
# argumentos a curl.exe por el codepage ANSI y rompe el UTF-8 del `-d`. La API
# recibe acentos sin problema (se comprueba mandando el mismo cuerpo con
# --data-binary @archivo); el que no los sabe pasar es el harness.
st=$(req POST "/admin/payments/$PID/refund" '{"amount":50000,"reason":"Falto una unidad"}')
assert_status 12c.3a "reembolso parcial" 200 "$st"
assert_eq 12c.3b "refund Succeeded" "Succeeded" "$(jqr '.state')"
assert_eq 12c.3c "monto reembolsado" "50000" "$(jqnum '.amount')"
assert_eq 12c.3d "orderRefunded acumulado" "50000" "$(jqnum '.orderRefunded')"
assert_eq 12c.3e "orderPaid intacto" "150000" "$(jqnum '.orderPaid')"

req GET "/admin/orders/$TO" >/dev/null
assert_eq 12c.4a "paid sigue siendo el bruto" "150000" "$(jqnum '.paid')"
assert_eq 12c.4b "refunded" "50000" "$(jqnum '.refunded')"
assert_eq 12c.4c "netPaid = paid - refunded" "100000" "$(jqnum '.netPaid')"
# El punto de todo el diseño: reembolsar NO deshace el cobro. PaymentStatus sigue
# registrando lo que se cobró, que es históricamente cierto.
assert_eq 12c.4d "paymentStatus sigue Paid" "Paid" "$(jqr '.paymentStatus')"
assert_eq 12c.4e "balance sin cambios" "0" "$(jqnum '.balance')"
req GET "/admin/orders/$TO/payments" >/dev/null
assert_eq 12c.4f "el cargo muestra lo reembolsado" "50000" "$(jqnum '.[0].refundedAmount')"

# Sin amount = el resto de lo que quede reembolsable en ese cargo.
st=$(req POST "/admin/payments/$PID/refund" '{}')
assert_status 12c.5a "reembolso del resto" 200 "$st"
assert_eq 12c.5b "toma el saldo restante" "100000" "$(jqnum '.amount')"
assert_eq 12c.5c "orderRefunded completo" "150000" "$(jqnum '.orderRefunded')"

st=$(req POST "/admin/payments/$PID/refund" '{"amount":1}')
assert_title 12c.6 "nada más que devolver" 409 "refund.exceeds_payment" "$st"

req GET "/admin/orders/$TO" >/dev/null
assert_eq 12c.7a "paid intacto tras devolver todo" "150000" "$(jqnum '.paid')"
assert_eq 12c.7b "refunded == paid" "150000" "$(jqnum '.refunded')"
assert_eq 12c.7c "netPaid 0" "0" "$(jqnum '.netPaid')"
assert_eq 12c.7d "paymentStatus NO retrocede" "Paid" "$(jqr '.paymentStatus')"
# Si PaymentStatus retrocediera, un pedido ya reembolsado quedaría cobrable otra vez.
st=$(pub POST "/orders/$TO/payments/initiate" '{"gateway":"Transfer","type":"Full"}')
assert_title 12c.7e "pedido reembolsado no se recobra" 409 "order.already_paid" "$st"

# Cancelar y reembolsar son dos decisiones distintas: cancelar libera el stock,
# reembolsar devuelve la plata. Un pedido cancelado sigue siendo reembolsable.
st=$(checkout "refund-cancel-$RUN@test.cl" "$VAR_SIMPLE" 1); RCO="$(jqr '.orderId')"
pub POST "/orders/$RCO/payments/initiate" '{"gateway":"Transfer","type":"Full"}' >/dev/null
RCP="$(jqr '.paymentId')"
req POST "/admin/payments/$RCP/confirm" '{"externalReference":"TRF-CANCEL"}' >/dev/null
st=$(req POST "/admin/orders/$RCO/cancel")
assert_status 12c.8a "cancelar pedido pagado" 200 "$st"
st=$(req POST "/admin/payments/$RCP/refund" '{"reason":"Pedido cancelado"}')
assert_status 12c.8b "reembolsar un pedido cancelado" 200 "$st"
assert_eq 12c.8c "devuelve todo lo cobrado" "150000" "$(jqnum '.amount')"
req GET "/admin/orders/$RCO" >/dev/null
assert_eq 12c.8d "queda cancelado y con netPaid 0" "0" "$(jqnum '.netPaid')"
assert_eq 12c.8e "fulfillment Cancelled" "Cancelled" "$(jqr '.fulfillmentStatus')"

# =============================================================================
# El comprador abonó y quedó debiendo el saldo, pero es un invitado: no tiene
# cuenta y no se le va a pedir que se registre. La tienda le manda un link por
# WhatsApp y con eso paga. Lo que se ejercita acá es que el backend resuelva la
# tienda correcta a partir del token, SIN header de tenant y sin auth.
#
# Se usa un pedido de stock con pago parcial en vez de una preventa: deja el
# mismo estado (Deposited + saldo pendiente) sin acoplar la sección al cupo del
# drop, y lo que se prueba es el link, no la mecánica de preventa (sección 10).
section "12d · Link de pago para invitados"
st=$(checkout "link-$RUN@test.cl" "$VAR_SIMPLE" 1); LO="$(jqr '.orderId')"
req POST "/admin/orders/$LO/payments" '{"amount":50000}' >/dev/null
assert_eq 12d.0a "abono parcial -> Deposited" "Deposited" "$(jqr '.paymentStatus')"
req GET "/admin/orders/$LO" >/dev/null
assert_eq 12d.0b "queda saldo" "100000" "$(jqnum '.balance')"

st=$(req POST "/admin/orders/$LO/payment-link" '{"validForDays":15}')
assert_status 12d.1a "la tienda genera el link" 200 "$st"
LINK_URL="$(jqr '.url')"; LTOKEN="${LINK_URL##*/}"
assert_eq 12d.1b "balance en la respuesta" "100000" "$(jqnum '.balance')"
[ -n "$(jqr '.expiresAt')" ] && ok 12d.1c "trae expiresAt" || ko 12d.1c "sin expiresAt"
case "$LINK_URL" in http://localhost:4200/pagar/*) ok 12d.1d "url sobre la base configurada";;
  *) ko 12d.1d "url inesperada: $LINK_URL";; esac
[ "${#LTOKEN}" = "43" ] && ok 12d.1e "token de 43 chars (32 bytes base64url)" \
  || ko 12d.1e "largo de token inesperado: ${#LTOKEN}"

# El invitado abre el link: sin X-Tenant-Id y sin auth. El tenant sale del token.
st=$(pub GET "/pay/$LTOKEN" "" "")
assert_status 12d.2a "abrir el link SIN header de tenant" 200 "$st"
assert_eq 12d.2b "ve su saldo" "100000" "$(jqnum '.balance')"
assert_eq 12d.2c "ve el total" "150000" "$(jqnum '.total')"
assert_eq 12d.2d "ve lo abonado" "50000" "$(jqnum '.paid')"
assert_eq 12d.2e "ve sus líneas" "1" "$(jqr '.lines | length')"
# Respuesta recortada a propósito: el que tiene el link no está autenticado.
assert_eq 12d.2f "no expone el customerId" "null" "$(jqr '.customerId')"
assert_eq 12d.2g "no expone fulfillmentStatus" "null" "$(jqr '.fulfillmentStatus')"
# El token manda sobre el header: aunque llegue el tenant equivocado, resuelve el suyo.
st=$(pub GET "/pay/$LTOKEN" "" "$TB")
assert_eq 12d.2h "el token gana sobre un header ajeno" "100000" "$(jqnum '.balance')"

st=$(pub GET "/pay/token-inventado" "" "")
assert_title 12d.3a "token inexistente" 404 "order.payment_link_invalid" "$st"
st=$(pub POST "/pay/token-inventado/initiate" '{"gateway":"Transfer"}' "")
assert_title 12d.3b "initiate con token inexistente" 404 "order.payment_link_invalid" "$st"

# Paga el saldo por transferencia, siempre sin identificarse.
st=$(pub POST "/pay/$LTOKEN/initiate" '{"gateway":"Transfer"}' "")
assert_status 12d.4a "initiate por el link" 200 "$st"
LPID="$(jqr '.paymentId')"
assert_eq 12d.4b "cobra el saldo exacto" "100000" "$(jqnum '.amount')"
[ "$(jqr '.bankDetails')" != "null" ] && ok 12d.4c "bankDetails para el invitado" || ko 12d.4c "sin bankDetails"

st=$(req POST "/admin/payments/$LPID/confirm" '{"externalReference":"TRF-LINK"}')
assert_eq 12d.5a "la tienda confirma -> Paid" "Paid" "$(jqr '.orderPaymentStatus')"
assert_eq 12d.5b "saldo 0" "0" "$(jqnum '.orderBalance')"

# El token dejó de revocarse al pagar: desde 4.3 es el ACCESO del invitado a su
# pedido, no solo permiso para pagarlo, y matarlo acá lo dejaría sin poder ver el
# pedido que acaba de pagar (no tiene cuenta con la cual entrar).
st=$(pub GET "/pay/$LTOKEN" "" "")
assert_status 12d.6a "el invitado sigue viendo su pedido pagado" 200 "$st"
assert_eq 12d.6b "y lo ve como pagado" "Paid" "$(jqr '.paymentStatus')"

# Se revoca al ENTREGAR: ahí sí se acabó lo que tenía que seguir. Y como nadie
# llama a revocar —lo hace el handler de OrderDelivered— esto sigue siendo la
# prueba de que los domain events se despachan.
req POST "/admin/orders/$LO/prepare" >/dev/null
LLINE="$(jqr '.lines[0].id')"
req GET "/admin/orders/$LO" >/dev/null; LLINE="$(jqr '.lines[0].id')"
req POST "/admin/orders/$LO/lines/$LLINE/fulfill" '{"quantity":1}' >/dev/null
st=$(pub GET "/pay/$LTOKEN" "" "")
assert_title 12d.6c "link revocado al entregar (OrderDelivered despachado)" 404 "order.payment_link_invalid" "$st"
st=$(req POST "/admin/orders/$LO/payment-link" '{}')
assert_title 12d.7 "no se genera link sin saldo" 409 "order.nothing_to_pay" "$st"

# Regenerar invalida el anterior: es la revocación, y sale gratis por guardar el hash.
st=$(checkout "link2-$RUN@test.cl" "$VAR_SIMPLE" 1); LO2="$(jqr '.orderId')"
req POST "/admin/orders/$LO2/payments" '{"amount":50000}' >/dev/null
req POST "/admin/orders/$LO2/payment-link" '{}' >/dev/null; T_OLD="$(jqr '.url')"; T_OLD="${T_OLD##*/}"
req POST "/admin/orders/$LO2/payment-link" '{}' >/dev/null; T_NEW="$(jqr '.url')"; T_NEW="${T_NEW##*/}"
[ "$T_OLD" != "$T_NEW" ] && ok 12d.8a "el token nuevo es distinto" || ko 12d.8a "mismo token dos veces"
st=$(pub GET "/pay/$T_OLD" "" ""); assert_title 12d.8b "el token viejo deja de servir" 404 "order.payment_link_invalid" "$st"
st=$(pub GET "/pay/$T_NEW" "" ""); assert_status 12d.8c "el token nuevo sirve" 200 "$st"

# Un pedido puede cancelarse DESPUÉS de mandar el link, y el link ya está en el
# chat del comprador. Sin esto se le podría cobrar un pedido muerto.
st=$(req POST "/admin/orders/$LO2/cancel"); assert_status 12d.9a "cancelar el pedido" 200 "$st"
st=$(pub GET "/pay/$T_NEW" "" "")
assert_title 12d.9b "el link deja de servir al cancelar" 404 "order.payment_link_invalid" "$st"
st=$(pub POST "/pay/$T_NEW/initiate" '{"gateway":"Transfer"}' "")
assert_title 12d.9c "tampoco se puede pagar" 404 "order.payment_link_invalid" "$st"
st=$(req POST "/admin/orders/$LO2/payment-link" '{}')
assert_title 12d.9d "no se genera link para un cancelado" 409 "order.cancelled" "$st"

st=$(req POST "/admin/orders/00000000-0000-0000-0000-000000000001/payment-link" '{}')
assert_title 12d.10a "pedido inexistente" 404 "order.not_found" "$st"
st=$(req POST "/admin/orders/$LO/payment-link" '{"validForDays":0}')
assert_status 12d.10b "validForDays 0" 400 "$st"
st=$(req POST "/admin/orders/$LO/payment-link" '{"validForDays":9999}')
assert_status 12d.10c "validForDays fuera de rango" 400 "$st"

# =============================================================================
# Mercado Pago se confirma por webhook, no por el navegador. Un e2e real necesita
# credenciales de una cuenta MP y un túnel público (MP no alcanza localhost), así
# que acá se cubre todo lo que NO depende de eso: los errores de initiate y el
# contrato HTTP del webhook, que es donde un bug se paga caro — un no-200 mete a
# MP en un loop de reintentos.
section "12b · Mercado Pago (sin credenciales reales)"
# Tienda efímera propia: configurar MP acá no ensucia $TA.
read -r MPO JAR_MP <<< "$(mkstore_session mp)"
creq POST /admin/products "{\"sku\":\"MP-$RUN\",\"name\":\"MP Test\",\"price\":25000,\"currency\":\"CLP\"}" "$MPO" "$JAR_MP" >/dev/null
PROD_MP="$(jqr '.')"
pub GET "/products/$PROD_MP" "" "$MPO" >/dev/null; VAR_MP="$(jqr '.variants[0].id')"
creq POST "/admin/variants/$VAR_MP/stock" '{"quantity":5}' "$MPO" "$JAR_MP" >/dev/null
pub POST /orders "{\"customer\":{\"email\":\"mp-$RUN@test.cl\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$VAR_MP\",\"quantity\":1}]}" "$MPO" >/dev/null
MPORDER="$(jqr '.orderId')"

st=$(creq PUT /admin/payment-methods/MercadoPago '{"credentialsJson":"{\"webhookSecret\":\"x\"}"}' "$MPO" "$JAR_MP")
assert_status 12b.1 "configurar MP sin accessToken" 200 "$st"
st=$(pub POST "/orders/$MPORDER/payments/initiate" '{"gateway":"MercadoPago","type":"Full"}' "$MPO")
assert_title 12b.2 "credenciales incompletas" 409 "payment.invalid_credentials" "$st"

st=$(creq PUT /admin/payment-methods/MercadoPago '{"credentialsJson":"{\"accessToken\":\"TEST-no-sirve\",\"webhookSecret\":\"\"}"}' "$MPO" "$JAR_MP")
st=$(pub POST "/orders/$MPORDER/payments/initiate" '{"gateway":"MercadoPago","type":"Full"}' "$MPO")
# Lo que importa: MP rechaza y devolvemos un error de dominio, no un 500.
assert_title 12b.3 "token inválido -> gateway_failure (no 500)" 409 "payment.gateway_failure" "$st"

# ---- contrato del webhook: SIEMPRE 200, pase lo que pase ---------------------
# El que llama es una máquina: un 500 no lo ayuda, solo garantiza reintentos.
HOOK="$API/payments/mercadopago/webhook/$MPO"
hook() { curl -s -o /dev/null -w '%{http_code}' -X POST "$@"; }
assert_eq 12b.4 "pago inexistente -> ack" "200" "$(hook "$HOOK?type=payment&data.id=123456789")"
assert_eq 12b.5 "topic no-payment -> ack" "200" "$(hook "$HOOK?type=merchant_order&data.id=999")"
assert_eq 12b.6 "id no numérico -> ack" "200" "$(hook "$HOOK?type=payment&data.id=abc")"
assert_eq 12b.7 "POST sin body ni query -> ack" "200" "$(hook "$HOOK")"
assert_eq 12b.8 "id en el body JSON -> ack" "200" \
  "$(hook "$HOOK" -H 'Content-Type: application/json' -d '{"type":"payment","data":{"id":"555"}}')"
assert_eq 12b.9 "body no-JSON -> ack" "200" \
  "$(hook "$HOOK" -H 'Content-Type: application/json' -d 'esto-no-es-json')"
# El tenant viaja en el path porque la llamada servidor-a-servidor no trae headers.
assert_eq 12b.10 "tenant inválido en el path -> 404" "404" \
  "$(hook "$API/payments/mercadopago/webhook/no-es-guid?type=payment&data.id=1")"

section "13 · Concurrencia (anti-sobreventa)"
st=$(req POST /admin/products "{\"sku\":\"RACE-$RUN\",\"name\":\"Race\",\"price\":5000,\"currency\":\"CLP\"}"); RP="$(jqr '.')"
req GET "/products/$RP" >/dev/null; RV="$(jqr '.variants[0].id')"
req POST "/admin/variants/$RV/stock" '{"quantity":1}' >/dev/null
declare -a CODES
for i in 1 2 3; do
  ( checkout "race$i-$RUN@test.cl" "$RV" 1 > "$BODY.r$i" ) &
done
wait
WINS=0
for i in 1 2 3; do c="$(cat "$BODY.r$i")"; [ "$c" = "200" ] && WINS=$((WINS+1)); rm -f "$BODY.r$i"; done
assert_eq 13.1 "exactamente un ganador" "1" "$WINS"
req GET "/variants/$RV/stock" >/dev/null
assert_eq 13.2a "available 0 (sin sobreventa)" "0" "$(jqr '.available')"
assert_eq 13.2b "reserved 1 (nunca > 1)" "1" "$(jqr '.reserved')"

section "14 · Transversales"
st=$(pub POST /orders "{bad json" "$TA")
assert_title 14.2 "JSON malformado -> 400 (F3)" 400 "request.invalid_body" "$st"
st=$(req GET "/admin/orders/no-es-guid"); assert_status 14.4 "guid inválido en ruta" 404 "$st"
AO=$(curl -s -o /dev/null -w '%{http_code}' -X OPTIONS "$API/products" \
  -H "Origin: http://localhost:4200" -H "Access-Control-Request-Method: POST" \
  -H "Access-Control-Request-Headers: x-tenant-id" -D "$BODY.h"; grep -i '^access-control-allow-origin' "$BODY.h" | tr -d '\r')
[ -n "$AO" ] && ok 14.5 "CORS preflight permite localhost:4200" || ko 14.5 "CORS preflight sin allow-origin"
curl -s -o /dev/null -X OPTIONS "$API/products" -H "Origin: http://evil.com" \
  -H "Access-Control-Request-Method: POST" -H "Access-Control-Request-Headers: x-tenant-id" -D "$BODY.h2"
grep -iq '^access-control-allow-origin' "$BODY.h2" && ko 14.6 "CORS permitió evil.com" || ok 14.6 "CORS bloquea origen no permitido"
rm -f "$BODY.h" "$BODY.h2"
st=$(req GET "/products/99999999-9999-9999-9999-999999999999")
[ "$(jqr '.title')" = "catalog.product_not_found" ] && [ "$(jqr '.status')" = "404" ] && ok 14.7 "ProblemDetails de dominio" || ko 14.7 "ProblemDetails de dominio"
st=$(req POST /admin/products '{"sku":"","name":"","price":-1,"currency":"X"}')
[ "$st" = "400" ] && [ "$(jqr '.errors | type')" = "object" ] && ok 14.8 "errores de validación agrupados" || ko 14.8 "errores de validación agrupados"

# =============================================================================
# Hasta acá el tenant era un GUID suelto en un header, sin nada detrás. Ahora las
# tiendas existen de verdad, y el id que devuelve POST /platform/stores ES el
# tenant id de todo el resto del sistema.
#
# Desde 4.3 exigen sesión: /platform la del operador, /admin/staff la de un Owner
# o Admin de esa misma tienda. Cada llamada lleva el tarro que le corresponde, y
# cuál corresponde es parte de lo que se prueba.
section "15 · Platform (tiendas y usuarios)"
SLUG="cardshop-$RUN"
st=$(plat POST /platform/stores \
  "{\"name\":\"Card Shop\",\"slug\":\"$SLUG\",\"ownerEmail\":\"dueno-$RUN@cardshop.cl\",\"ownerName\":\"Dueno\",\"ownerPassword\":\"password-larga-123\"}" "")
assert_status 15.1a "crear tienda SIN header de tenant" 200 "$st"
S1="$(jqr '.id')"
assert_eq 15.1b "slug normalizado" "$SLUG" "$(jqr '.slug')"
assert_eq 15.1c "nace activa" "Active" "$(jqr '.status')"
assert_eq 15.1d "sin dominio propio" "null" "$(jqr '.customDomain')"

# El slug es un label DNS: va a ser un subdominio, así que se valida como tal.
st=$(plat POST /platform/stores \
  "{\"name\":\"x\",\"slug\":\"Con Mayusculas Y Espacios\",\"ownerEmail\":\"a-$RUN@x.cl\",\"ownerName\":\"A\",\"ownerPassword\":\"password-larga-123\"}" "")
assert_status 15.2a "slug inválido" 400 "$st"
st=$(plat POST /platform/stores \
  "{\"name\":\"x\",\"slug\":\"otro-$RUN\",\"ownerEmail\":\"no-es-email\",\"ownerName\":\"A\",\"ownerPassword\":\"password-larga-123\"}" "")
assert_status 15.2b "email de owner inválido" 400 "$st"
st=$(plat POST /platform/stores \
  "{\"name\":\"x\",\"slug\":\"otro2-$RUN\",\"ownerEmail\":\"b-$RUN@x.cl\",\"ownerName\":\"A\",\"ownerPassword\":\"corta\"}" "")
assert_status 15.2c "password de owner muy corta" 400 "$st"
st=$(plat POST /platform/stores \
  "{\"name\":\"Otra\",\"slug\":\"$SLUG\",\"ownerEmail\":\"c-$RUN@x.cl\",\"ownerName\":\"A\",\"ownerPassword\":\"password-larga-123\"}" "")
assert_title 15.2d "slug repetido" 409 "platform.slug_taken" "$st"

# La tienda nace con su dueño: una tienda que nadie puede administrar no sirve.
JAR_S1="$BODY.jar.s1"; JAR_S2="$BODY.jar.s2"; touch "$JAR_S1" "$JAR_S2"
creq POST /auth/staff/login "{\"email\":\"dueno-$RUN@cardshop.cl\",\"password\":\"password-larga-123\"}" "$S1" "$JAR_S1" >/dev/null
st=$(creq GET /admin/staff "" "$S1" "$JAR_S1")
assert_status 15.3a "listar staff de la tienda nueva" 200 "$st"
assert_eq 15.3b "nace con exactamente un usuario" "1" "$(jqr '. | length')"
assert_eq 15.3c "y es el Owner" "Owner" "$(jqr '.[0].role')"
assert_eq 15.3d "email del owner normalizado" "dueno-$RUN@cardshop.cl" "$(jqr '.[0].email')"
# El hash no sale nunca, ni para el admin que lo acaba de fijar.
assert_eq 15.3e "no expone el passwordHash" "null" "$(jqr '.[0].passwordHash')"

st=$(creq POST /admin/staff \
  "{\"email\":\"caja-$RUN@cardshop.cl\",\"name\":\"Cajera\",\"password\":\"password-larga-456\",\"role\":\"Cashier\"}" "$S1" "$JAR_S1")
assert_status 15.4a "sumar una cajera" 200 "$st"
assert_eq 15.4b "rol Cashier (listo para el POS de fase 5)" "Cashier" "$(jqr '.role')"
CASHIER_ID="$(jqr '.id')"
st=$(creq POST /admin/staff \
  "{\"email\":\"CAJA-$RUN@cardshop.cl\",\"name\":\"Otra\",\"password\":\"password-larga-789\",\"role\":\"Staff\"}" "$S1" "$JAR_S1")
assert_title 15.4c "email repetido en la misma tienda" 409 "platform.staff_email_taken" "$st"

# Aislamiento: el staff SÍ es tenant-scoped, así que otra tienda no lo ve.
st=$(plat POST /platform/stores \
  "{\"name\":\"Otra Tienda\",\"slug\":\"otra-$RUN\",\"ownerEmail\":\"dueno2-$RUN@otra.cl\",\"ownerName\":\"Dueno2\",\"ownerPassword\":\"password-larga-123\"}" "")
S2="$(jqr '.id')"
creq POST /auth/staff/login "{\"email\":\"dueno2-$RUN@otra.cl\",\"password\":\"password-larga-123\"}" "$S2" "$JAR_S2" >/dev/null
creq GET /admin/staff "" "$S2" "$JAR_S2" >/dev/null
assert_eq 15.5a "la otra tienda solo ve su owner" "1" "$(jqr '. | length')"
assert_eq 15.5b "y es el suyo" "dueno2-$RUN@otra.cl" "$(jqr '.[0].email')"
# El mismo email puede trabajar en dos tiendas: la unicidad es por tienda.
st=$(creq POST /admin/staff \
  "{\"email\":\"caja-$RUN@cardshop.cl\",\"name\":\"Cajera\",\"password\":\"password-larga-456\",\"role\":\"Cashier\"}" "$S2" "$JAR_S2")
assert_status 15.5c "el mismo email en otra tienda sí se puede" 200 "$st"

# Store NO es tenant-scoped: listar tiendas las trae todas, sin filtro.
st=$(plat GET /platform/stores "" "")
assert_status 15.6a "listar tiendas sin tenant" 200 "$st"
[ "$(jqr "[.[] | select(.id==\"$S1\")] | length")" = "1" ] && ok 15.6b "la tienda 1 aparece" || ko 15.6b "falta la tienda 1"
[ "$(jqr "[.[] | select(.id==\"$S2\")] | length")" = "1" ] && ok 15.6c "la tienda 2 aparece (sin filtro de tenant)" || ko 15.6c "falta la tienda 2"

# Suspender/activar: el único enforcement de suscripción que existe.
st=$(plat POST "/platform/stores/$S2/suspend" "" "")
assert_eq 15.7a "suspender" "Suspended" "$(jqr '.status')"
st=$(plat POST "/platform/stores/$S2/activate" "" "")
assert_eq 15.7b "reactivar" "Active" "$(jqr '.status')"
st=$(plat POST "/platform/stores/00000000-0000-0000-0000-000000000001/suspend" "" "")
assert_title 15.7c "tienda inexistente" 404 "platform.store_not_found" "$st"

# El staff sí exige tenant; el guard sigue puesto para todo lo que no sea platform.
st=$(pub POST /admin/staff \
  "{\"email\":\"x-$RUN@x.cl\",\"name\":\"X\",\"password\":\"password-larga-123\",\"role\":\"Staff\"}" "$S1")
assert_status 15.8 "crear staff sin sesión" 401 "$st"

# =============================================================================
# El mecanismo de sesiones. Todavía NO está aplicado a los endpoints existentes
# (eso es 4.3): acá se prueba que el mecanismo en sí funciona y, sobre todo, que
# una sesión legítima de una tienda no sirve contra otra.
#
# $S1 y $S2 vienen de la sección 15, con sus owners y sus contraseñas conocidas.
section "16 · Autenticación por sesión"
JARP2="$BODY.jar.platform2"; JARS="$BODY.jar.staff"; JARX="$BODY.jar.other"
rm -f "$JARP2" "$JARS" "$JARX"; touch "$JARP2" "$JARS" "$JARX"

# --- operador de plataforma (el sembrado en 4.1) ---
st=$(creq POST /auth/platform/login "{\"email\":\"$OPERATOR_EMAIL\",\"password\":\"$OPERATOR_PASSWORD\"}" "" "$JARP2")
assert_status 16.1a "login de operador" 200 "$st"
assert_eq 16.1b "devuelve el nombre" "Platform Admin" "$(jqr '.displayName')"
# El token va SOLO en la cookie: nunca en el cuerpo, donde un proxy lo loguearía.
assert_eq 16.1c "el token no viaja en el body" "null" "$(jqr '.token')"
grep -q "lewik_platform_session" "$JARP2" && ok 16.1d "dejó la cookie de plataforma" || ko 16.1d "sin cookie"
grep -q "^#HttpOnly_" "$JARP2" && ok 16.1e "cookie HttpOnly (fuera del alcance de JS)" || ko 16.1e "cookie no es HttpOnly"

st=$(creq GET /auth/platform/me "" "" "$JARP2")
assert_status 16.2a "usar la sesión de plataforma" 200 "$st"
assert_eq 16.2b "es quien dice ser" "Platform Admin" "$(jqr '.name')"

# --- staff ---
st=$(creq POST /auth/staff/login "{\"email\":\"dueno-$RUN@cardshop.cl\",\"password\":\"password-larga-123\"}" "$S1" "$JARS")
assert_status 16.3a "login de staff" 200 "$st"
assert_eq 16.3b "rol Owner" "Owner" "$(jqr '.role')"
assert_eq 16.3c "el token no viaja en el body" "null" "$(jqr '.token')"
grep -q "lewik_panel_session" "$JARS" && ok 16.3d "dejó la cookie del panel" || ko 16.3d "sin cookie"

st=$(creq GET /auth/staff/me "" "$S1" "$JARS")
assert_status 16.4a "usar la sesión de staff" 200 "$st"
assert_eq 16.4b "rol en los claims" "Owner" "$(jqr '.role')"
assert_eq 16.4c "tenant en los claims" "$S1" "$(jqr '.tenantId')"

# ⭐ LA PRUEBA QUE IMPORTA: la MISMA cookie, apuntando a OTRA tienda.
# Sin esto, el dueño de una tienda opera la de otro cambiando el subdominio.
st=$(creq GET /auth/staff/me "" "$S2" "$JARS")
assert_status 16.5a "sesión válida contra OTRA tienda -> 403" 403 "$st"
# 403, no 401: las credenciales son buenas, la tienda no. Un 401 mandaría a
# reloguear, que no arregla nada y esconde el problema real.
st=$(creq GET /auth/staff/me "" "" "$JARS")
assert_status 16.5b "sesión sin tenant resuelto -> 403" 403 "$st"

# Cruzar poblaciones: son schemes distintos, con cookies y tablas distintas.
st=$(creq GET /auth/platform/me "" "" "$JARS")
assert_status 16.6a "cookie de staff en endpoint de plataforma -> 401" 401 "$st"
st=$(creq GET /auth/staff/me "" "$S1" "$JARP2")
assert_status 16.6b "cookie de plataforma en endpoint de staff -> 401" 401 "$st"

# Sin cookie y con cookie inventada.
st=$(pub GET /auth/staff/me "" "$S1")
assert_status 16.7a "sin sesión -> 401" 401 "$st"
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/auth/staff/me" \
  -H "X-Tenant-Id: $S1" -H "Cookie: lewik_panel_session=token-inventado")
assert_status 16.7b "token inventado -> 401" 401 "$st"

# --- revocación instantánea (el requisito del POS) ---
# OJO: no alcanza con volver a pedir /me con el mismo tarro de cookies. El logout
# borra la cookie del cliente, así que ese request sale SIN cookie y da 401 aunque
# el servidor no haya revocado nada — la prueba pasaría igual con la revocación
# rota. Al que le robaron el token no le sirve que el navegador de la víctima haya
# limpiado su tarro: hay que reproducir el token crudo contra el servidor.
STOK="$(awk '/lewik_panel_session/ {print $7}' "$JARS")"
[ -n "$STOK" ] && ok 16.8a "token extraído del tarro para reproducirlo" || ko 16.8a "no se pudo leer el token"
replay() { curl -s -o "$BODY" -w '%{http_code}' "$API$1" -H "X-Tenant-Id: $2" -H "Cookie: $3=$4"; }
st=$(replay /auth/staff/me "$S1" lewik_panel_session "$STOK")
assert_status 16.8b "el token crudo funciona antes del logout" 200 "$st"

st=$(creq POST /auth/staff/logout "" "$S1" "$JARS")
assert_status 16.8c "logout" 204 "$st"
# Server-side: el token robado deja de servir en el acto porque el logout borró la
# entrada de caché. Sin ese RemoveAsync seguiría vivo hasta el TTL.
st=$(replay /auth/staff/me "$S1" lewik_panel_session "$STOK")
assert_status 16.8d "el token robado muere en el acto (caché invalidada)" 401 "$st"
# Y aparte, el cliente se queda sin cookie.
grep -q "lewik_panel_session" "$JARS" && ko 16.8e "la cookie sobrevivió al logout" || ok 16.8e "el logout borra la cookie del cliente"
st=$(creq POST /auth/staff/logout "" "$S1" "$JARS")
assert_status 16.8f "logout de nuevo es inofensivo" 204 "$st"

# --- credenciales malas: siempre el mismo error ---
st=$(pub POST /auth/staff/login "{\"email\":\"dueno-$RUN@cardshop.cl\",\"password\":\"incorrecta\"}" "$S1")
assert_title 16.9a "password incorrecta" 400 "auth.invalid_credentials" "$st"
st=$(pub POST /auth/staff/login "{\"email\":\"no-existe-$RUN@cardshop.cl\",\"password\":\"password-larga-123\"}" "$S1")
assert_title 16.9b "email inexistente: MISMO error (no enumera cuentas)" 400 "auth.invalid_credentials" "$st"
# El owner de $S1 no existe en $S2, aunque la contraseña sea válida en su tienda.
st=$(pub POST /auth/staff/login "{\"email\":\"dueno-$RUN@cardshop.cl\",\"password\":\"password-larga-123\"}" "$S2")
assert_title 16.9c "credenciales de otra tienda" 400 "auth.invalid_credentials" "$st"
st=$(pub POST /auth/platform/login "{\"email\":\"$OPERATOR_EMAIL\",\"password\":\"incorrecta\"}" "")
assert_title 16.9d "operador con password mala" 400 "auth.invalid_credentials" "$st"

# --- una tienda suspendida no deja entrar ---
plat POST "/platform/stores/$S2/suspend" "" "" >/dev/null
st=$(pub POST /auth/staff/login "{\"email\":\"dueno2-$RUN@otra.cl\",\"password\":\"password-larga-123\"}" "$S2")
assert_title 16.10a "login en tienda suspendida" 409 "platform.store_suspended" "$st"
plat POST "/platform/stores/$S2/activate" "" "" >/dev/null
st=$(creq POST /auth/staff/login "{\"email\":\"dueno2-$RUN@otra.cl\",\"password\":\"password-larga-123\"}" "$S2" "$JARX")
assert_status 16.10b "y sí deja al reactivarla" 200 "$st"

# --- desactivar a la persona mata sus sesiones ---
# El join de LoadAsync exige IsActive, así que la sesión deja de cargar.
st=$(creq GET /auth/staff/me "" "$S2" "$JARX")
assert_status 16.11a "la sesión de la otra tienda funciona" 200 "$st"
assert_eq 16.11b "y es de SU tienda" "$S2" "$(jqr '.tenantId')"

# --- desde 4.3 el mecanismo ya protege de verdad ---
st=$(pub GET /platform/stores "" "")
assert_status 16.12a "/platform/stores ya NO está abierto" 401 "$st"
st=$(plat GET /platform/stores "" "")
assert_status 16.12b "y sí responde al operador" 200 "$st"
st=$(pub GET /products "" "$TA")
assert_status 16.12c "el catálogo sigue anónimo (no rompimos la vidriera)" 200 "$st"
rm -f "$JARP2" "$JARS" "$JARX"; touch "$JARP2" "$JARS" "$JARX"

# =============================================================================
# El límite de seguridad ahora está en la URL: lo público es público, lo de
# gestión vive bajo /admin, y adentro de /admin hay tres alturas (staff, admin,
# owner). Esta sección prueba las alturas, no las rutas una por una — eso ya lo
# cubren las secciones 1 a 14, que corren enteras con sesión.
section "17 · Partición y privilegios"
JAR_CAJA="$BODY.jar.caja"; touch "$JAR_CAJA"

# Una cajera de la tienda $S1 (creada en la sección 15).
st=$(creq POST /auth/staff/login \
  "{\"email\":\"caja-$RUN@cardshop.cl\",\"password\":\"password-larga-456\"}" "$S1" "$JAR_CAJA")
assert_status 17.1a "login de la cajera" 200 "$st"
assert_eq 17.1b "rol Cashier" "Cashier" "$(jqr '.role')"

# ⭐ ESCALADA DE PRIVILEGIO: la cajera tiene sesión válida de ESTA tienda, pero
# cambiar las credenciales de pasarela redirige la plata de la tienda a otra
# cuenta. Es el privilegio más peligroso del sistema y es solo del dueño.
st=$(creq PUT /admin/payment-methods/Transfer '{"credentialsJson":"{}"}' "$S1" "$JAR_CAJA")
assert_status 17.2a "cajera NO toca las pasarelas (owner-only)" 403 "$st"
# Y tampoco crea usuarios: eso es de admin para arriba.
st=$(creq POST /admin/staff \
  "{\"email\":\"colada-$RUN@x.cl\",\"name\":\"C\",\"password\":\"password-larga-123\",\"role\":\"Owner\"}" "$S1" "$JAR_CAJA")
assert_status 17.2b "cajera NO crea usuarios (admin-only)" 403 "$st"
# Pero sí opera: para eso entró. Lo que se afirma es que NO la corta la política
# (403); que el recurso no exista en su tienda (404) significa que pasó el control
# y llegó al handler, que es justo lo que se quiere ver.
st=$(creq GET "/admin/products/$PROD_SIMPLE/purchase-limit" "" "$S1" "$JAR_CAJA")
if [ "$st" != "403" ] && [ "$st" != "401" ]; then
  ok 17.2c "cajera SÍ entra a la operación (HTTP $st)"
else
  ko 17.2c "cajera bloqueada de más (HTTP $st)"
fi
# El dueño de la misma tienda sí puede.
st=$(creq PUT /admin/payment-methods/Transfer \
  '{"credentialsJson":"{\"banco\":\"Estado\",\"numero\":\"1\",\"titular\":\"T\"}"}' "$S1" "$JAR_S1")
assert_status 17.2d "el dueño SÍ toca las pasarelas" 200 "$st"

# --- sin sesión no se entra a nada de gestión ---
st=$(pub POST /admin/products "{\"sku\":\"NOAUTH-$RUN\",\"name\":\"x\",\"price\":1,\"currency\":\"CLP\"}")
assert_status 17.3a "crear producto sin sesión" 401 "$st"
st=$(pub GET "/admin/orders/$TO")
assert_status 17.3b "leer un pedido sin sesión" 401 "$st"
st=$(pub POST "/admin/variants/$VAR_SIMPLE/stock" '{"quantity":1}')
assert_status 17.3c "reponer stock sin sesión" 401 "$st"
st=$(pub GET /admin/staff "" "$S1")
assert_status 17.3d "listar staff sin sesión" 401 "$st"

# --- la vidriera sigue abierta: el storefront no tiene cuenta ---
st=$(pub GET /products); assert_status 17.4a "catálogo público" 200 "$st"
st=$(pub GET "/products/$PROD_SIMPLE"); assert_status 17.4b "ficha de producto pública" 200 "$st"
st=$(pub GET "/variants/$VAR_SIMPLE/stock"); assert_status 17.4c "disponibilidad pública" 200 "$st"
st=$(pub GET "/variants/$VAR_DROP/preorder"); assert_status 17.4d "cupo del drop público" 200 "$st"

# --- el header CSRF: capa extra sobre SameSite y CORS estricto ---
# GET no lo exige (rompería navegar), los que cambian estado sí.
st=$(curl -s -o "$BODY" -w '%{http_code}' -X POST "$API/admin/products" -b "$JAR_A" \
  -H "X-Tenant-Id: $TA" -H "Content-Type: application/json" \
  -d "{\"sku\":\"NOCSRF-$RUN\",\"name\":\"x\",\"price\":1,\"currency\":\"CLP\"}")
assert_title 17.5a "POST sin header CSRF" 403 "request.csrf_header_missing" "$st"
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/admin/orders/$TO" -b "$JAR_A" -H "X-Tenant-Id: $TA")
assert_status 17.5b "GET no exige CSRF" 200 "$st"

# --- el invitado hace todo su recorrido sin sesión ---
st=$(pub POST /orders "{\"customer\":{\"email\":\"guest-$RUN@test.cl\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$VAR_SIMPLE\",\"quantity\":1}]}")
assert_status 17.6a "checkout anónimo" 200 "$st"
GO="$(jqr '.orderId')"; GTOK="$(jqr '.accessToken')"
[ -n "$GTOK" ] && [ "$GTOK" != "null" ] && ok 17.6b "el checkout le da su token de acceso" || ko 17.6b "sin accessToken"
st=$(pub GET "/pay/$GTOK" "" "")
assert_status 17.6c "ve su pedido con el token" 200 "$st"
assert_eq 17.6d "y es el suyo" "$GO" "$(jqr '.orderId')"
# Sin el token no hay forma: el GUID pelado ya no alcanza.
st=$(pub GET "/admin/orders/$GO")
assert_status 17.6e "el GUID pelado ya no abre el pedido" 401 "$st"

# --- desactivar a alguien le corta la sesión en el acto ---
CTOK="$(awk '/lewik_panel_session/ {print $7}' "$JAR_CAJA")"
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/auth/staff/me" -H "X-Tenant-Id: $S1" \
  -H "Cookie: lewik_panel_session=$CTOK")
assert_status 17.7a "la cajera está adentro" 200 "$st"
st=$(creq POST "/admin/staff/$CASHIER_ID/deactivate" "" "$S1" "$JAR_S1")
assert_status 17.7b "el dueño la desactiva" 200 "$st"
assert_eq 17.7c "queda inactiva" "false" "$(jqr '.isActive')"
# Con el token crudo, para no medir el borrado de cookie sino la revocación real.
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/auth/staff/me" -H "X-Tenant-Id: $S1" \
  -H "Cookie: lewik_panel_session=$CTOK")
assert_status 17.7d "su sesión muere en el acto (no al vencer la caché)" 401 "$st"
st=$(pub POST /auth/staff/login \
  "{\"email\":\"caja-$RUN@cardshop.cl\",\"password\":\"password-larga-456\"}" "$S1")
assert_title 17.7e "y tampoco puede volver a entrar" 409 "auth.account_disabled" "$st"
rm -f "$JAR_CAJA"

# =============================================================================
# La tienda deja de identificarse por un header que elige quien llama y pasa a salir del
# dominio de la request. Todo esto se prueba con `dom`, es decir SIN X-Tenant-Id: es la
# única forma de que las aserciones digan algo sobre producción, donde el header no existe.
#
# En dev Tenancy:BaseDomain es "localhost" y *.localhost resuelve a 127.0.0.1 sin tocar
# el archivo hosts, así que smoke-a-123.localhost es el mismo camino que cardshop.lewik.app.
section "18 · Resolución de tenant por dominio"
SLUG_A="smoke-a-$RUN"; SLUG_B="smoke-b-$RUN"

# --- las formas que tienen que caer en la misma tienda ---
st=$(dom GET /health/tenant "$SLUG_A.localhost")
assert_eq 18.1a "el slug resuelve la tienda, sin ningún header" "$TA" "$(jqr '.tenantId')"
st=$(dom GET /health/tenant "panel.$SLUG_A.localhost")
assert_eq 18.1b "panel. es routing del front: misma tienda" "$TA" "$(jqr '.tenantId')"
st=$(dom GET /health/tenant "www.$SLUG_A.localhost")
assert_eq 18.1c "www. también" "$TA" "$(jqr '.tenantId')"
st=$(dom GET /health/tenant "$SLUG_B.localhost")
assert_eq 18.1d "otro slug, otra tienda" "$TB" "$(jqr '.tenantId')"

# --- y las que no tienen que resolver nada ---
st=$(dom GET /health/tenant "noexiste-$RUN.localhost")
assert_eq 18.2a "host desconocido: sin tenant" "no tenant resolved" "$(jqr '.message')"
st=$(dom GET /health/tenant "panel.localhost")
assert_eq 18.2b "subdominio reservado de la plataforma" "no tenant resolved" "$(jqr '.message')"
st=$(dom GET /health/tenant "api.localhost")
assert_eq 18.2c "api. tampoco es una tienda" "no tenant resolved" "$(jqr '.message')"
# Adivinar cuál de tres etiquetas es el slug apuntaría un host que no publicamos a una
# tienda real; bajo el dominio base solo existen slug.base y panel.slug.base.
st=$(dom GET /health/tenant "a.b.$SLUG_A.localhost")
assert_eq 18.2d "forma no soportada: no resuelve por las dudas" "no tenant resolved" "$(jqr '.message')"
st=$(dom GET /health/tenant "otro.$SLUG_A.localhost")
assert_eq 18.2e "prefijo desconocido: tampoco" "no tenant resolved" "$(jqr '.message')"

# --- dominio propio de la tienda (el caso www.tienda.cl) ---
DOM="smoke-$RUN.cl"
st=$(plat POST /platform/stores \
  "{\"name\":\"Smoke Dominio\",\"slug\":\"smoke-d-$RUN\",\"customDomain\":\"$DOM\",\"ownerEmail\":\"owner-d-$RUN@smoke.cl\",\"ownerName\":\"Owner D\",\"ownerPassword\":\"$OWNER_PASSWORD\"}" "")
TD="$(jqr '.id')"
assert_status 18.3a "crear tienda con dominio propio" 200 "$st"
st=$(dom GET /health/tenant "$DOM")
assert_eq 18.3b "el dominio propio resuelve" "$TD" "$(jqr '.tenantId')"
st=$(dom GET /health/tenant "www.$DOM")
assert_eq 18.3c "con www. (registró el dominio pelado)" "$TD" "$(jqr '.tenantId')"
st=$(dom GET /health/tenant "panel.$DOM")
assert_eq 18.3d "y el panel del cliente" "$TD" "$(jqr '.tenantId')"
st=$(dom GET /health/tenant "smoke-d-$RUN.localhost")
assert_eq 18.3e "el slug le sigue sirviendo igual" "$TD" "$(jqr '.tenantId')"
st=$(dom GET /health/tenant "otro-$RUN.cl")
assert_eq 18.3f "un dominio de nadie no resuelve" "no tenant resolved" "$(jqr '.message')"

# --- la vidriera pública sale del dominio: es lo que ve el comprador ---
st=$(dom GET "/products/$PROD_SIMPLE" "$SLUG_A.localhost")
assert_status 18.4a "ficha de producto por dominio" 200 "$st"
# Lo que prueba el aislamiento no es el 200 de arriba sino este 404: el mismo GUID, el
# mismo backend, otro dominio, y el producto no existe.
st=$(dom GET "/products/$PROD_SIMPLE" "$SLUG_B.localhost")
assert_status 18.4b "el producto de A no existe bajo el dominio de B" 404 "$st"

# --- login del staff por dominio: el camino real del panel ---
JAR_DOM="$BODY.jar.dom"; touch "$JAR_DOM"
st=$(dom POST /auth/staff/login "panel.$SLUG_A.localhost" \
  "{\"email\":\"owner-a-$RUN@smoke.cl\",\"password\":\"$OWNER_PASSWORD\"}" "$JAR_DOM")
assert_status 18.5a "login sin header de tenant, solo el dominio" 200 "$st"
st=$(dom GET "/admin/orders/$TO" "panel.$SLUG_A.localhost" "" "$JAR_DOM")
assert_status 18.5b "y esa sesión opera el panel de su tienda" 200 "$st"
# ⭐ La garantía de 4.2, ahora expresada como lo que un atacante haría de verdad: no
# inventar un header, sino apuntar la MISMA sesión al subdominio de otra tienda.
#
# Con el token crudo y no con el tarro: el navegador nunca mandaría esa cookie a otro
# dominio, así que usar el tarro mediría la política de cookies del cliente y no la del
# servidor. Acá el servidor recibe una credencial buena para la tienda equivocada, que es
# exactamente lo que ve si a alguien le roban la cookie o la copia a mano.
DTOK="$(awk '/lewik_panel_session/ {print $7}' "$JAR_DOM")"
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/admin/orders/$TO" \
  -H "Host: panel.$SLUG_B.localhost" -H "Cookie: lewik_panel_session=$DTOK")
assert_status 18.5c "esa sesión contra el dominio de otra tienda" 403 "$st"
# 403 y no 401: la credencial es válida: lo que no corresponde es la tienda.
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/admin/orders/$TO" \
  -H "Host: noexiste-$RUN.localhost" -H "Cookie: lewik_panel_session=$DTOK")
assert_status 18.5d "y contra un dominio que no resuelve nada tampoco" 403 "$st"

# --- suspender corta la tienda en el acto ---
# Primero se calienta la caché a propósito: si estas rutas nunca se hubieran resuelto,
# el 403 de más abajo saldría de un cache miss y no probaría nada sobre la invalidación.
st=$(dom GET /products "smoke-d-$RUN.localhost"); assert_status 18.6a "la tienda D atiende" 200 "$st"
st=$(dom GET /products "$DOM"); assert_status 18.6b "también por su dominio propio" 200 "$st"
st=$(plat POST "/platform/stores/$TD/suspend" "" "")
assert_eq 18.6c "el operador la suspende" "Suspended" "$(jqr '.status')"
st=$(dom GET /products "smoke-d-$RUN.localhost")
assert_title 18.6d "cortada al instante, no al vencer el TTL" 403 "platform.store_suspended" "$st"
st=$(dom GET /products "$DOM")
assert_title 18.6e "por su dominio propio también" 403 "platform.store_suspended" "$st"
# El staff no se topa con una pared muda: llega al login y le dicen por qué.
st=$(dom POST /auth/staff/login "panel.smoke-d-$RUN.localhost" \
  "{\"email\":\"owner-d-$RUN@smoke.cl\",\"password\":\"$OWNER_PASSWORD\"}")
assert_title 18.6f "el dueño llega al login y le explican" 409 "platform.store_suspended" "$st"
# ⭐ El comprador NO. Hasta 4.7 la allowlist era el prefijo "/auth" pelado, así que el login de
# clientes seguía abierto en una tienda que contesta 403 a todo lo demás. Con credenciales
# inventadas: antes llegaba al handler y devolvía 400 auth.invalid_credentials — o sea, atendía.
st=$(dom POST /auth/customer/login "smoke-d-$RUN.localhost" \
  '{"email":"comprador@test.cl","password":"lo-que-sea"}')
assert_title 18.6f2 "la tienda suspendida está cerrada al comprador" 403 "platform.store_suspended" "$st"
st=$(dom GET /health/db "smoke-d-$RUN.localhost")
assert_status 18.6g "health sigue arriba (suspendida no es rota)" 200 "$st"
st=$(dom GET /health/entitlement "smoke-d-$RUN.localhost")
assert_eq 18.6h "y el entitlement dice que no" "false" "$(jqr '.isActive')"
st=$(plat POST "/platform/stores/$TD/activate" "" "")
assert_eq 18.6i "reactivar" "Active" "$(jqr '.status')"
st=$(dom GET /products "smoke-d-$RUN.localhost")
assert_status 18.6j "y vuelve a atender en el acto" 200 "$st"

# --- el entitlement ya no es un stub que decía que sí a cualquiera ---
st=$(pub GET /health/entitlement "" "$TA")
assert_eq 18.7a "entitlement de una tienda real" "true" "$(jqr '.isActive')"
assert_eq 18.7b "sin plan inventado" "null" "$(jqr '.planCode')"
# Con el stub esto devolvía 200 y "stub-pro" para cualquier GUID.
st=$(pub GET /health/entitlement "" "$(guid)")
assert_status 18.7c "un tenant que no existe no tiene entitlement" 404 "$st"

# --- el header sigue existiendo, y en dev manda ---
# Documenta la precedencia: si el header está habilitado, gana sobre el dominio. Por eso
# la app se niega a arrancar con esto prendido fuera de Development.
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/health/tenant" \
  -H "Host: $SLUG_B.localhost" -H "X-Tenant-Id: $TA")
assert_eq 18.8 "en dev el header le gana al dominio" "$TA" "$(jqr '.tenantId')"

# =============================================================================
# La tercera población: el comprador. Todo por dominio (`dom`), porque el storefront es
# justamente lo que vive en un dominio y no manda headers de tenant.
#
# OJO: los tarros de cookies quedan atados al Host que mandamos, así que un token de una
# población no llega solo a otra — hay que repetirlo a mano para probar que el servidor lo
# rechaza, y no que el cliente no lo mandó.
section "19 · Compradores: invitado y con cuenta"
CH="$SLUG_A.localhost"                      # el storefront de la tienda A
JAR_C="$BODY.jar.cust"; JAR_C2="$BODY.jar.cust2"; JAR_C3="$BODY.jar.cust3"
touch "$JAR_C" "$JAR_C2" "$JAR_C3"
CEMAIL="juan-$RUN@test.cl"; CPASS="micontrasena123"

# Un producto propio de esta sección, para no chocar con los límites que ya dejaron puestos
# las secciones 6 y 18 sobre PROD_SIMPLE.
st=$(req POST /admin/products "{\"sku\":\"CUST-$RUN\",\"name\":\"Deck Box\",\"price\":9990,\"currency\":\"CLP\"}")
PROD_CUST="$(jqr '.')"
st=$(req GET "/products/$PROD_CUST"); VAR_CUST="$(jqr '.variants[0].id')"
req POST "/admin/variants/$VAR_CUST/stock" '{"quantity":50,"reason":"cust section"}' >/dev/null
[ -n "$VAR_CUST" ] && [ "$VAR_CUST" != "null" ] && ok 19.0 "producto de la sección listo" || ko 19.0 "sin variante"

# --- 1. compra como INVITADO, sin cuenta y sin nada ---
st=$(dom POST /orders "$CH" \
  "{\"customer\":{\"email\":\"$CEMAIL\",\"phone\":\"+56911111111\",\"name\":\"Juan\"},\"items\":[{\"productVariantId\":\"$VAR_CUST\",\"quantity\":1}]}")
assert_status 19.1a "checkout de invitado por dominio" 200 "$st"
GORD="$(jqr '.orderId')"
[ -n "$GORD" ] && [ "$GORD" != "null" ] && ok 19.1b "el invitado se lleva su pedido" || ko 19.1b "sin orderId"

# --- 2. se registra CON EL MISMO EMAIL: promueve, no duplica ---
st=$(dom POST /auth/customer/register "$CH" \
  "{\"email\":\"$CEMAIL\",\"password\":\"$CPASS\",\"phone\":\"+56911111111\",\"name\":\"Juan\"}" "$JAR_C")
assert_status 19.2a "registro con un email que ya compró de invitado" 200 "$st"
assert_eq 19.2b "es la misma persona" "$CEMAIL" "$(jqr '.email')"
assert_eq 19.2c "el token no viaja en el body" "null" "$(jqr '.token')"
grep -q "lewik_store_session" "$JAR_C" && ok 19.2d "dejó la cookie del storefront" || ko 19.2d "sin cookie"
grep -q "^#HttpOnly_" "$JAR_C" && ok 19.2e "cookie HttpOnly" || ko 19.2e "cookie no es HttpOnly"

# --- 3. ⭐ y encuentra su compra ANTERIOR de invitado dentro de su cuenta ---
# Esto es lo que se cobra del modelo unificado de 2.4.3: un solo Customer con o sin
# contraseña. Si el registro hubiera creado otra fila, esta lista vendría vacía.
st=$(dom GET /account/orders "$CH" "" "$JAR_C")
assert_status 19.3a "ve sus pedidos" 200 "$st"
assert_eq 19.3b "conserva el historial de cuando era invitado" "1" "$(jqr "[.[] | select(.id==\"$GORD\")] | length")"

# --- 4. compra logueado, sin volver a dar datos de contacto ---
st=$(dom POST /orders "$CH" "{\"items\":[{\"productVariantId\":\"$VAR_CUST\",\"quantity\":1}]}" "$JAR_C")
assert_status 19.4a "checkout logueado sin datos de contacto" 200 "$st"
LORD="$(jqr '.orderId')"
st=$(dom GET /account/orders "$CH" "" "$JAR_C")
assert_eq 19.4b "queda asociado a su cuenta" "1" "$(jqr "[.[] | select(.id==\"$LORD\")] | length")"
assert_eq 19.4c "y ahora tiene dos" "2" "$(jqr 'length')"
# Sin sesión los datos de contacto siguen siendo obligatorios.
st=$(dom POST /orders "$CH" "{\"items\":[{\"productVariantId\":\"$VAR_CUST\",\"quantity\":1}]}")
assert_status 19.4d "de invitado, sin contacto -> 400" 400 "$st"

# --- 5. registrarse de nuevo ---
st=$(dom POST /auth/customer/register "$CH" \
  "{\"email\":\"$CEMAIL\",\"password\":\"otra-clave-123\",\"phone\":\"+56911111111\",\"name\":\"Juan\"}")
assert_title 19.5 "el mismo email dos veces" 409 "customer.already_registered" "$st"

# --- 6. login, y una revocación que se mide de verdad ---
st=$(dom POST /auth/customer/login "$CH" "{\"email\":\"$CEMAIL\",\"password\":\"$CPASS\"}" "$JAR_C2")
assert_status 19.6a "login del comprador" 200 "$st"
st=$(dom GET /account/me "$CH" "" "$JAR_C2"); assert_status 19.6b "usa la sesión" 200 "$st"
# Mayúsculas en el email: el mismo cliente.
st=$(dom POST /auth/customer/login "$CH" "{\"email\":\"JUAN-$RUN@TEST.CL\",\"password\":\"$CPASS\"}" "")
assert_status 19.6c "login con el email en mayúsculas" 200 "$st"
st=$(dom POST /auth/customer/login "$CH" "{\"email\":\"$CEMAIL\",\"password\":\"clave-equivocada\"}" "")
assert_title 19.6d "contraseña equivocada" 400 "auth.invalid_credentials" "$st"
# Un email que NO existe da el mismo error, palabra por palabra.
st=$(dom POST /auth/customer/login "$CH" "{\"email\":\"nadie-$RUN@test.cl\",\"password\":\"$CPASS\"}" "")
assert_title 19.6e "email inexistente: error idéntico" 400 "auth.invalid_credentials" "$st"
# Un invitado tampoco puede entrar: no tiene contraseña, y no se le dice eso.
st=$(dom POST /orders "$CH" \
  "{\"customer\":{\"email\":\"guest-$RUN@test.cl\",\"phone\":\"+56900000000\"},\"items\":[{\"productVariantId\":\"$VAR_CUST\",\"quantity\":1}]}" )
st=$(dom POST /auth/customer/login "$CH" "{\"email\":\"guest-$RUN@test.cl\",\"password\":\"$CPASS\"}" "")
assert_title 19.6f "un invitado no tiene con qué entrar" 400 "auth.invalid_credentials" "$st"
# Logout: con el token crudo, para medir la revocación del servidor y no el borrado de cookie.
CTOK2="$(awk '/lewik_store_session/ {print $7}' "$JAR_C2")"
st=$(dom POST /auth/customer/logout "$CH" "" "$JAR_C2"); assert_status 19.6g "logout" 204 "$st"
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/account/me" \
  -H "Host: $CH" -H "Cookie: lewik_store_session=$CTOK2")
assert_status 19.6h "su sesión muere en el acto, no al vencer la caché" 401 "$st"

# --- 7. ⭐ las tres poblaciones están selladas ---
# Con el token crudo bajo el nombre de cookie de la OTRA población: si esto pasara, las
# cookies distintas serían decoración y lo que separa a un comprador de un cajero sería
# solo que el navegador no manda la cookie.
CTOK="$(awk '/lewik_store_session/ {print $7}' "$JAR_C")"
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/admin/orders/$TO" \
  -H "Host: panel.$SLUG_A.localhost" -H "Cookie: lewik_panel_session=$CTOK")
assert_status 19.7a "token de comprador en la cookie del panel" 401 "$st"
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/account/orders" \
  -H "Host: $CH" -H "Cookie: lewik_store_session=$DTOK")
assert_status 19.7b "token de staff en la cookie del storefront" 401 "$st"
# Y con su propia cookie, cada uno en la puerta del otro.
st=$(dom GET "/admin/orders/$TO" "$CH" "" "$JAR_C")
assert_status 19.7c "comprador en el panel" 401 "$st"
st=$(dom GET /account/orders "panel.$SLUG_A.localhost" "" "$JAR_DOM")
assert_status 19.7d "staff en la cuenta del comprador" 401 "$st"
# El operador de plataforma tampoco es un comprador.
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/account/orders" \
  -H "Host: $CH" -H "Cookie: lewik_store_session=$(awk '/lewik_platform_session/ {print $7}' "$JARP")")
assert_status 19.7e "token de plataforma en la cookie del storefront" 401 "$st"

# --- 8. un comprador no ve los pedidos de otro ---
st=$(dom POST /auth/customer/register "$CH" \
  "{\"email\":\"ana-$RUN@test.cl\",\"password\":\"clave-de-ana-123\",\"phone\":\"+56922222222\",\"name\":\"Ana\"}" "$JAR_C3")
assert_status 19.8a "se registra otra compradora" 200 "$st"
st=$(dom POST /orders "$CH" "{\"items\":[{\"productVariantId\":\"$VAR_CUST\",\"quantity\":1}]}" "$JAR_C3")
AORD="$(jqr '.orderId')"
st=$(dom GET "/account/orders/$AORD" "$CH" "" "$JAR_C3")
assert_status 19.8b "Ana ve su pedido" 200 "$st"
# 404 y no 403: un 403 confirmaría que el pedido existe.
st=$(dom GET "/account/orders/$AORD" "$CH" "" "$JAR_C")
assert_title 19.8c "Juan pide el pedido de Ana" 404 "order.not_found" "$st"
st=$(dom GET /account/orders "$CH" "" "$JAR_C3")
assert_eq 19.8d "y la lista de Ana es solo la de Ana" "1" "$(jqr 'length')"

# --- 9. ⭐ un cliente de A no es cliente de B ---
st=$(curl -s -o "$BODY" -w '%{http_code}' "$API/account/orders" \
  -H "Host: $SLUG_B.localhost" -H "Cookie: lewik_store_session=$CTOK")
assert_status 19.9 "la sesión de Juan contra el dominio de otra tienda" 403 "$st"

# --- 10. ⭐ registrarse NO resetea los topes anti-scalping ---
# Es el mismo Customer, así que el historial es el mismo. Vale probarlo porque un modelo
# con dos filas (invitado y registrado) daría dos contadores y un tope de dos por cliente
# se compraría dos veces.
LIMLOW="lim-$RUN@test.cl"; LIMUP="LIM-$RUN@TEST.CL"
# SKU CAP-, no LIM-: la sección 6 ya usa LIM-$RUN para su producto sonda y el SKU es único
# por tienda, así que repetirlo daba un 409 que se arrastraba hasta el final de la sección.
st=$(req POST /admin/products "{\"sku\":\"CAP-$RUN\",\"name\":\"Alt Art\",\"price\":50000,\"currency\":\"CLP\"}")
PROD_LIM="$(jqr '.')"
assert_status 19.10a0 "crear el producto con tope" 200 "$st"
st=$(req GET "/products/$PROD_LIM"); VAR_LIM="$(jqr '.variants[0].id')"
req POST "/admin/variants/$VAR_LIM/stock" '{"quantity":30,"reason":"limit section"}' >/dev/null
st=$(req PUT "/admin/products/$PROD_LIM/purchase-limit" '{"maxPerOrder":2,"maxPerCustomer":2,"windowDays":30}')
assert_eq 19.10a "tope de 2 por cliente" "2" "$(jqnum '.maxPerCustomer')"

st=$(dom POST /orders "$CH" \
  "{\"customer\":{\"email\":\"$LIMLOW\",\"phone\":\"+56933333333\"},\"items\":[{\"productVariantId\":\"$VAR_LIM\",\"quantity\":2}]}")
assert_status 19.10b "de invitado compra las 2 que le tocan" 200 "$st"
st=$(dom POST /orders "$CH" \
  "{\"customer\":{\"email\":\"$LIMLOW\",\"phone\":\"+56933333333\"},\"items\":[{\"productVariantId\":\"$VAR_LIM\",\"quantity\":1}]}")
assert_title 19.10c "la tercera no" 409 "order.limit_per_customer" "$st"
# ⭐ Con el email en MAYÚSCULAS. El índice (tenant, email) compara con case, así que sin
# normalizar el email esto sería otro cliente con el contador en cero.
st=$(dom POST /orders "$CH" \
  "{\"customer\":{\"email\":\"$LIMUP\",\"phone\":\"+56933333333\"},\"items\":[{\"productVariantId\":\"$VAR_LIM\",\"quantity\":1}]}")
assert_title 19.10d "cambiar mayúsculas no da un contador nuevo" 409 "order.limit_per_customer" "$st"
# Y ahora se registra: promueve la misma fila, así que el contador sigue donde estaba.
JAR_LIM="$BODY.jar.lim"; touch "$JAR_LIM"
st=$(dom POST /auth/customer/register "$CH" \
  "{\"email\":\"$LIMLOW\",\"password\":\"clave-larga-123\",\"phone\":\"+56933333333\"}" "$JAR_LIM")
assert_status 19.10e "se registra con ese email" 200 "$st"
st=$(dom GET /account/orders "$CH" "" "$JAR_LIM")
assert_eq 19.10f "hereda la compra que hizo de invitado" "1" "$(jqr 'length')"
st=$(dom POST /orders "$CH" "{\"items\":[{\"productVariantId\":\"$VAR_LIM\",\"quantity\":1}]}" "$JAR_LIM")
assert_title 19.10g "registrarse no le devuelve el cupo" 409 "order.limit_per_customer" "$st"

# =============================================================================
# La ventana blanda: un pedido sin pagar no retiene stock para siempre. Lo que se prueba acá es
# el barredor de verdad (su consulta, el tenant que fija por pedido, y el CancelOrderCommand que
# reusa), no un reloj de mentira.
#
# Cómo, sin esperar los 30 minutos del TTL: se corre el vencimiento hacia atrás en la base con
# psql y se espera un ciclo de barrido. Bajar el TTL de Development a un minuto sería peor —
# medio script trabaja sobre un pedido sin pagar a lo largo de varias secciones (el $ORDER de la
# 7 se paga en la 9, el link de pago de la 12d), así que el barredor los cancelaría por debajo y
# harían rojo secciones que no tienen nada que ver con reservas.
section "20 · Expiración de reservas (soft-lock)"
PSQL="docker exec lewik_store_db psql -U lewik -d lewik_store -t -A -q"
EXPIRY_WAIT="${EXPIRY_WAIT:-20}"          # SweepIntervalSeconds de Development (10) + margen

# backdate ORDER_ID -> deja el vencimiento 5 minutos en el pasado
backdate() { $PSQL -c "UPDATE orders SET reservation_expires_at = now() - interval '5 minutes' WHERE id = '$1';" >/dev/null 2>&1; }

if ! $PSQL -c "SELECT 1;" >/dev/null 2>&1; then
  echo "  (omitida: no se pudo alcanzar Postgres con 'docker exec lewik_store_db psql')"
else
  # Producto propio: PROD_SIMPLE arrastra los topes de las secciones 6 y 18.
  st=$(req POST /admin/products "{\"sku\":\"EXP-$RUN\",\"name\":\"Booster Box\",\"price\":80000,\"currency\":\"CLP\"}")
  PROD_EXP="$(jqr '.')"
  st=$(req GET "/products/$PROD_EXP"); VAR_EXP="$(jqr '.variants[0].id')"
  req POST "/admin/variants/$VAR_EXP/stock" '{"quantity":20,"reason":"expiry section"}' >/dev/null
  st=$(req POST /admin/products "{\"sku\":\"EXPD-$RUN\",\"name\":\"Drop Sellado\",\"price\":30000,\"currency\":\"CLP\"}")
  PROD_EXPD="$(jqr '.')"
  st=$(req GET "/products/$PROD_EXPD"); VAR_EXPD="$(jqr '.variants[0].id')"
  req PUT "/admin/variants/$VAR_EXPD/preorder" \
    '{"capacity":50,"releaseDate":"2026-12-01T00:00:00Z","depositType":"Percentage","depositValue":30}' >/dev/null
  [ -n "$VAR_EXP" ] && [ "$VAR_EXP" != "null" ] && [ "$VAR_EXPD" != "null" ] \
    && ok 20.0 "productos de la sección listos" || ko 20.0 "sin variantes"

  req GET "/variants/$VAR_EXP/stock" >/dev/null; ER0="$(jqr '.reserved')"

  # --- A. el caso central: reserva sin pagar, se vence, el stock vuelve ---
  st=$(checkout "exp-a-$RUN@test.cl" "$VAR_EXP" 2)
  OA="$(jqr '.orderId')"; ATOK="$(jqr '.accessToken')"
  assert_status 20.1a "checkout" 200 "$st"
  [ "$(jqr '.reservationExpiresAt')" != "null" ] \
    && ok 20.1b "el checkout devuelve el plazo (el front cuenta con esto)" \
    || ko 20.1b "sin reservationExpiresAt en la respuesta del checkout"
  req GET "/variants/$VAR_EXP/stock" >/dev/null
  assert_eq 20.1c "reserva +2" "$((ER0+2))" "$(jqr '.reserved')"
  # El plazo también viaja en la vista del invitado, que es quien corre contra ese reloj: sin
  # esto su primera noticia de la ventana es un pago rechazado.
  st=$(pub GET "/pay/$ATOK" "" "")
  [ "$(jqr '.reservationExpiresAt')" != "null" ] \
    && ok 20.1d "el invitado ve su plazo en /pay/{token}" \
    || ko 20.1d "sin plazo en la vista del invitado"

  # --- B. ⭐ un pago parcial apaga el reloj para siempre ---
  st=$(checkout "exp-b-$RUN@test.cl" "$VAR_EXP" 2); OB="$(jqr '.orderId')"
  st=$(req POST "/admin/orders/$OB/payments" '{"amount":50000}')
  assert_eq 20.2a "pago parcial -> Deposited" "Deposited" "$(jqr '.paymentStatus')"
  req GET "/admin/orders/$OB" >/dev/null
  assert_eq 20.2b "el pago borra el plazo" "null" "$(jqr '.reservationExpiresAt')"

  # --- C. iniciar el pago extiende la ventana ---
  # Un pedido recién hecho tiene los 30 minutos completos, más que la gracia de 20: ahí extender no
  # tiene nada que hacer y no debe recortar nada. El caso que importa es el contrario, el comprador
  # que llega al final de su ventana y recién entonces va a pagar, así que se lo pone a 90 segundos
  # del vencimiento. (90 y no negativo: con el plazo ya pasado el barredor lo cancelaría entre el
  # UPDATE y el initiate y la prueba mediría una carrera.)
  st=$(checkout "exp-c-$RUN@test.cl" "$VAR_EXP" 1); OC="$(jqr '.orderId')"
  $PSQL -c "UPDATE orders SET reservation_expires_at = now() + interval '90 seconds' WHERE id = '$OC';" >/dev/null
  req GET "/admin/orders/$OC" >/dev/null; DL_BEFORE="$(jqr '.reservationExpiresAt')"
  st=$(pub POST "/orders/$OC/payments/initiate" '{"gateway":"Transfer","type":"Full"}')
  assert_status 20.3a "initiate" 200 "$st"
  req GET "/admin/orders/$OC" >/dev/null; DL_AFTER="$(jqr '.reservationExpiresAt')"
  # Comparación lexicográfica: los dos vienen del mismo serializador, en UTC y con el mismo formato.
  [ "$DL_AFTER" \> "$DL_BEFORE" ] \
    && ok 20.3b "el plazo se corrió hacia adelante" \
    || ko 20.3b "el plazo no se movió ($DL_BEFORE -> $DL_AFTER)"
  # Y se corrió hasta la gracia, no un poco: de 90 segundos a más de 10 minutos.
  assert_eq 20.3c "quedó a la distancia de PaymentGraceMinutes" "t" \
    "$($PSQL -c "SELECT reservation_expires_at > now() + interval '10 minutes' FROM orders WHERE id = '$OC';" | tr -d '[:space:]')"

  # --- D. la preventa también libera cupo ---
  req GET "/variants/$VAR_EXPD/preorder" >/dev/null; ESD0="$(jqr '.soldCount')"
  st=$(pub POST /orders "{\"customer\":{\"email\":\"exp-d-$RUN@test.cl\",\"phone\":\"+56944444444\"},\"items\":[{\"productVariantId\":\"$VAR_EXPD\",\"quantity\":3}]}")
  OD="$(jqr '.orderId')"; assert_status 20.4a "checkout de preventa" 200 "$st"
  req GET "/variants/$VAR_EXPD/preorder" >/dev/null
  assert_eq 20.4b "cupo vendido +3" "$((ESD0+3))" "$(jqr '.soldCount')"

  # --- una sola espera para los cuatro escenarios ---
  # Snapshot con las tres reservas puestas (A=2, B=2, C=1): así el delta que se mide después es
  # exactamente lo que soltó A, sin depender de lo que haya reservado el resto del script.
  req GET "/variants/$VAR_EXP/stock" >/dev/null; EA1="$(jqr '.available')"; ER1="$(jqr '.reserved')"
  # Los vencimientos se corren recién ACÁ, después del snapshot y no junto a cada escenario: el
  # barredor pasa cada 10s y el armado de los cuatro casos son ~15 llamadas, así que un backdate
  # temprano puede ser barrido ANTES de medir y el delta da cero. Se vio de verdad, con la API
  # ralentizada a propósito. Puesto acá, la ventana entre el snapshot y el vencimiento es nula.
  # (B se vence a mano igual: el filtro que lo salva es su estado de pago, no el campo vacío.)
  backdate "$OA"; backdate "$OB"; backdate "$OD"
  printf "  esperando %ss un ciclo del barredor...\n" "$EXPIRY_WAIT"
  sleep "$EXPIRY_WAIT"

  # A: ⭐ el stock vuelve solo, sin que nadie haga nada
  req GET "/admin/orders/$OA" >/dev/null
  assert_eq 20.5a "el pedido vencido queda Cancelled" "Cancelled" "$(jqr '.fulfillmentStatus')"
  assert_eq 20.5b "y sin plazo" "null" "$(jqr '.reservationExpiresAt')"
  req GET "/variants/$VAR_EXP/stock" >/dev/null
  assert_eq 20.5c "vuelven las 2 unidades a available" "$((EA1+2))" "$(jqr '.available')"
  assert_eq 20.5d "y salen de reserved (B y C siguen reservando)" "$((ER1-2))" "$(jqr '.reserved')"

  # B: ⭐ la plata manda
  req GET "/admin/orders/$OB" >/dev/null
  assert_eq 20.6a "con plata adentro NO se cancela" "PendingPayment" "$(jqr '.fulfillmentStatus')"
  assert_eq 20.6b "y sigue Deposited" "Deposited" "$(jqr '.paymentStatus')"

  # C: la gracia del pago lo protegió
  req GET "/admin/orders/$OC" >/dev/null
  assert_eq 20.7 "el que está pagando sigue vivo" "PendingPayment" "$(jqr '.fulfillmentStatus')"

  # D: el cupo del drop vuelve igual que el stock
  req GET "/variants/$VAR_EXPD/preorder" >/dev/null
  assert_eq 20.8a "soldCount vuelve a $ESD0" "$ESD0" "$(jqr '.soldCount')"
  req GET "/admin/orders/$OD" >/dev/null
  assert_eq 20.8b "la preventa vencida queda Cancelled" "Cancelled" "$(jqr '.fulfillmentStatus')"

  # --- ⭐ nadie va a la pasarela por un pedido ya cancelado ---
  # Sin esto el comprador paga con tarjeta un pedido que ya no existe y la plata queda cobrada
  # esperando un reembolso a mano. Antes era un caso raro (cancelar era un acto humano); con el
  # vencimiento automático deja de serlo.
  st=$(pub POST "/orders/$OA/payments/initiate" '{"gateway":"Transfer","type":"Full"}')
  assert_title 20.9 "initiate sobre un pedido cancelado" 409 "order.cancelled" "$st"
fi

# =============================================================================
section "21 · Storefront compuesto y disponibilidad en vivo"
HOST_A="$SLUG_A.localhost"; HOST_B="$SLUG_B.localhost"

# Productos propios: la sección mide números exactos en el frame que llega, así que no puede
# compartir variantes con lo que haya reservado o soltado el resto del script.
st=$(req POST /admin/products "{\"sku\":\"LIVE-$RUN\",\"name\":\"ZZ Live Stock\",\"price\":15000,\"currency\":\"CLP\"}")
PROD_LIVE="$(jqr '.')"
st=$(req GET "/products/$PROD_LIVE"); VLIVE="$(jqr '.variants[0].id')"
req POST "/admin/variants/$VLIVE/stock" '{"quantity":40,"reason":"live section"}' >/dev/null
st=$(req POST /admin/products "{\"sku\":\"LIVED-$RUN\",\"name\":\"ZZ Live Drop\",\"price\":25000,\"currency\":\"CLP\"}")
PROD_LIVED="$(jqr '.')"
st=$(req GET "/products/$PROD_LIVED"); VLIVED="$(jqr '.variants[0].id')"
req PUT "/admin/variants/$VLIVED/preorder" \
  '{"capacity":60,"releaseDate":"2026-12-01T00:00:00Z","depositType":"Percentage","depositValue":30}' >/dev/null
# Con las tres partes puestas, para que 21.1f2 mida un filtrado y no un campo que estaba vacío.
req PUT "/admin/variants/$VLIVE/purchase-limit" '{"maxPerOrder":6,"maxPerCustomer":9,"windowDays":30}' >/dev/null

# --- 21.1 el read compuesto: catálogo + disponibilidad + topes en UNA llamada ---
st=$(dom GET /storefront "$HOST_A")
assert_status 21.1a "GET /storefront por dominio" 200 "$st"
assert_eq 21.1b "trae la tienda que resolvió el host" "$SLUG_A" "$(jqr '.store.slug')"
# Lo que hace útil al endpoint no es que responda, sino que NINGUNA variante quede sin
# disponibilidad: una sola que falte devuelve el front a las N+1 llamadas que esto vino a matar.
assert_eq 21.1c "toda variante trae disponibilidad" "0" \
  "$(jqr '[.products[].variants[] | select(.availability == null)] | length')"
assert_eq 21.1d "el stock se ve como Stock, con su número" "Stock 40" \
  "$(jqr "[.products[].variants[] | select(.id==\"$VLIVE\")][0] | \"\(.availability.kind) \(.availability.available)\"")"
# El drop trae además lo que la página de un drop necesita para existir: fecha y abono.
assert_eq 21.1e "el drop se ve como Preorder con cupo, fecha y abono" "Preorder 60 Percentage 30" \
  "$(jqr "[.products[].variants[] | select(.id==\"$VLIVED\")][0] | \"\(.availability.kind) \(.availability.available) \(.availability.depositType) \(.availability.depositValue)\"")"
assert_eq 21.1f "el tope por pedido viaja (el front capa el selector)" "6" \
  "$(jqr "[.products[].variants[] | select(.id==\"$VLIVE\")][0].limit.maxPerOrder")"
# ⭐ Y la política NO viaja entera. maxPerOrder se descubre igual pidiendo de más; maxPerCustomer
# y windowDays son la receta completa para un revendedor: cuántas cuentas hacer y cada cuánto
# rotarlas. El tope por cliente sigue existiendo y sigue aplicándose en el checkout (19.10c), solo
# que no se anuncia.
assert_eq 21.1f2 "⭐ el tope por cliente NO se publica" "null null" \
  "$(jqr "[.products[].variants[] | select(.id==\"$VLIVE\")][0].limit | \"\(.maxPerCustomer) \(.windowDays)\"")"
# Y el panel lo sigue viendo entero: la política no se perdió, cambió de audiencia.
req GET "/admin/variants/$VLIVE/purchase-limit" >/dev/null
assert_eq 21.1f3 "el panel sí ve la política completa" "6 9 30" \
  "$(jqr '"\(.maxPerOrder) \(.maxPerCustomer) \(.windowDays)"')"
# El aislamiento del read, dicho como una ausencia: mismo backend, otro dominio, no está.
st=$(dom GET /storefront "$HOST_B")
assert_eq 21.1g "la variante de A no aparece en la vidriera de B" "0" \
  "$(jqr "[.products[].variants[] | select(.id==\"$VLIVE\")] | length")"
# Un host que no es de nadie no es una tienda vacía: es un 404. Con TenantId vacío no hay
# tienda que coincida, así que falla cerrado en vez de mostrar un catálogo sin dueño.
st=$(dom GET /storefront "noexiste-$RUN.localhost")
assert_title 21.1h "host desconocido: 404, no una vidriera vacía" 404 "platform.store_not_found" "$st"

# --- 21.2 el hub también es de una tienda ---
IDA="$(hub_open "$HOST_A")" || IDA=""
[ -n "$IDA" ] && ok 21.2a "el hub acepta la conexión por dominio" || ko 21.2a "no se pudo abrir la conexión"
# Suspendida no atiende tampoco por el hub: hasta 4.7 /hubs estaba en la lista de rutas sin
# tenant, así que era la única puerta que seguía abierta con la tienda cortada.
st=$(plat POST "/platform/stores/$TD/suspend" "" "")
neg=$(curl -s -o "$BODY" -w '%{http_code}' -X POST "$API/hubs/store/negotiate?negotiateVersion=1" -H "Host: smoke-d-$RUN.localhost")
assert_title 21.2b "tienda suspendida: el hub la rechaza" 403 "platform.store_suspended" "$neg"
plat POST "/platform/stores/$TD/activate" "" "" >/dev/null

# --- 21.3 suscribirse ya trae el número, y el número baja solo ---
IDB="$(hub_open "$HOST_B")" || IDB=""
# Las dos conexiones miran EL MISMO variantId. La de B no debería recibir nada, y no porque el
# id no exista de su lado: el grupo lleva el tenant, así que su suscripción apunta a un grupo
# al que nadie emite jamás.
SNAP="$(hub_invoke "$IDA" "$HOST_A" WatchVariant "$VLIVE")"
hub_av() { printf '%s' "$1" | jq -r '.result | "\(.kind) \(.available) \(.isSellable)"' 2>/dev/null; }
# ⭐ Suscribirse devuelve el estado actual en la misma llamada. Es lo que borra la carrera: leer
# por un lado y suscribirse por otro pierde cualquier cambio que entre en el medio, y el contador
# queda viejo para siempre sin que nadie se entere.
assert_eq 21.3a "⭐ suscribirse ya devuelve el estado actual" "Stock 40 true" "$(hub_av "$SNAP")"
# Y dice exactamente lo mismo que la vidriera, porque las dos derivan del mismo factory: dos
# definiciones de "disponible" es lo que hace que una página muestre un número y el checkout otro.
st=$(dom GET /storefront "$HOST_A")
assert_eq 21.3b "el hub y la vidriera coinciden al carácter" \
  "$(jqr "[.products[].variants[] | select(.id==\"$VLIVE\")][0].availability | \"\(.kind) \(.available) \(.isSellable)\"")" \
  "$(hub_av "$SNAP")"
# ⭐ El mismo id desde la otra tienda: ahora que el hub CONSULTA, el filtro de tenant también
# tiene que sostenerse acá, no solo el nombre del grupo. Lee 0, no los 40 de A.
SNAPB="$(hub_invoke "$IDB" "$HOST_B" WatchVariant "$VLIVE")"
assert_eq 21.3c "⭐ la otra tienda no lee el stock ajeno al suscribirse" "Stock 0 false" "$(hub_av "$SNAPB")"

st=$(dom POST /orders "$HOST_A" \
  "{\"customer\":{\"email\":\"live-$RUN@test.cl\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$VLIVE\",\"quantity\":3}]}")
assert_status 21.3d "checkout sobre la variante mirada" 200 "$st"
FRAME="$(hub_poll "$IDA" "$HOST_A" 10 | grep availabilityChanged | head -1)"
assert_eq 21.3e "⭐ el frame llega solo, sin que nadie pregunte" "Stock 37 true" \
  "$(printf '%s' "$FRAME" | jq -r '.arguments[0] | "\(.kind) \(.available) \(.isSellable)"' 2>/dev/null)"
# La ausencia se mide con el mismo evento ya entregado del otro lado: si acá llega algo, el
# aislamiento por grupo no existe.
OTHER="$(hub_poll "$IDB" "$HOST_B" 5)"
[ -z "$OTHER" ] && ok 21.3f "⭐ a la otra tienda no le llega nada" \
  || ko 21.3f "fuga entre tiendas: $(printf '%s' "$OTHER" | head -c 120)"

# --- 21.4 ⭐ el caso estrella: el cupo de un drop en vivo ---
# Es lo que 4.7 vino a arreglar. Preorder era una Entity y no levantaba eventos, así que durante
# un drop —el único momento donde un contador en vivo importa— no se movía nada.
SNAP="$(hub_invoke "$IDA" "$HOST_A" WatchVariant "$VLIVED")"
# La misma llamada resuelve la otra forma de vender: preventa activa gana sobre stock, igual que
# en la vidriera y que en el checkout.
assert_eq 21.4a "suscribirse a un drop devuelve su cupo" "Preorder 60 true" "$(hub_av "$SNAP")"
st=$(dom POST /orders "$HOST_A" \
  "{\"customer\":{\"email\":\"live-drop-$RUN@test.cl\",\"phone\":\"+56922222222\"},\"items\":[{\"productVariantId\":\"$VLIVED\",\"quantity\":4}]}")
ODROP="$(jqr '.orderId')"
assert_status 21.4b "checkout del drop" 200 "$st"
FRAME="$(hub_poll "$IDA" "$HOST_A" 10 | grep availabilityChanged | head -1)"
assert_eq 21.4c "⭐ el cupo baja en vivo" "Preorder 56 true" \
  "$(printf '%s' "$FRAME" | jq -r '.arguments[0] | "\(.kind) \(.available) \(.isSellable)"' 2>/dev/null)"

# --- 21.5 y sube solo cuando el cupo vuelve ---
# El mismo camino que recorre una reserva vencida de 4.6: nadie mira la pantalla, el cupo vuelve.
req POST "/admin/orders/$ODROP/cancel" >/dev/null
FRAME="$(hub_poll "$IDA" "$HOST_A" 10 | grep availabilityChanged | head -1)"
assert_eq 21.5 "⭐ cancelar devuelve el cupo y el contador sube" "Preorder 60 true" \
  "$(printf '%s' "$FRAME" | jq -r '.arguments[0] | "\(.kind) \(.available) \(.isSellable)"' 2>/dev/null)"

# --- 21.6 lo que no se mira, no se manda ---
# Sin esto, "llegó un frame" no probaría que hay filtrado: podría estar emitiendo a todo el
# mundo. Un solo pedido mueve LAS DOS variantes, con una sola dada de baja, así que la prueba
# es una comparación dentro del mismo poll y no depende de tiempos.
hub_invoke "$IDA" "$HOST_A" UnwatchVariant "$VLIVED"
st=$(dom POST /orders "$HOST_A" \
  "{\"customer\":{\"email\":\"live-mix-$RUN@test.cl\",\"phone\":\"+56933333333\"},\"items\":[{\"productVariantId\":\"$VLIVE\",\"quantity\":1},{\"productVariantId\":\"$VLIVED\",\"quantity\":1}]}")
assert_status 21.6a "un pedido que mueve las dos variantes" 200 "$st"
FRAMES="$(hub_poll "$IDA" "$HOST_A" 10)"
assert_eq 21.6b "sigue llegando lo suscrito" "1" \
  "$(printf '%s' "$FRAMES" | grep -c "$VLIVE\"" || true)"
assert_eq 21.6c "y nada de lo que se dio de baja" "0" \
  "$(printf '%s' "$FRAMES" | grep -c "$VLIVED\"" || true)"

# =============================================================================
section "RESUMEN"
printf "  ${G}PASS: %d${Z}   ${R}FAIL: %d${Z}   ${Y}WARN: %d${Z}   (RUN=%s)\n" "$PASS" "$FAIL" "$WARN" "$RUN"
[ "$WARN" -gt 0 ] && echo "  (WARN = zonas de riesgo pendientes; tras F1–F8 deberían ser 0)"
[ "$FAIL" -eq 0 ] && exit 0 || exit 1
