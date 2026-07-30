#!/usr/bin/env bash
# =============================================================================
# LeWiK Store API — seed determinista para los E2E del front
#
#   ./scripts/seed-e2e.sh                    # resetea y siembra
#   API=http://host:puerto ./scripts/seed-e2e.sh
#   ./scripts/seed-e2e.sh --no-reset         # siembra sin borrar (falla si ya existe)
#   OUT=ruta.json ./scripts/seed-e2e.sh      # dónde escribir el manifiesto
#
# Por qué existe: la vidriera, el checkout, el pago y el contador en vivo son TODOS
# anónimos, así que el front no necesita autenticarse para probarlos — pero sí necesita
# que haya algo que mostrar, y sembrar catálogo, stock y drops sí requiere sesión de
# plataforma y de staff. Este script hace esa parte una vez y deja un manifiesto JSON
# con todos los ids, para que la suite del front no dependa de GUIDs escritos a mano.
#
# DETERMINISTA a propósito: slugs, SKUs, emails y contraseñas son fijos, no llevan
# timestamp. Correrlo de nuevo borra lo anterior y reconstruye lo mismo, así que un test
# puede afirmar "quedan 100 cupos" sin cuidar el orden ni limpiar después.
#
# Requisitos: API arriba, `jq`, y `docker exec lewik_store_db psql` para el reset (no hay
# endpoint que borre tiendas, y no debería haberlo).
# =============================================================================
set -u

API="${API:-http://localhost:5223}"
OPERATOR_EMAIL="${OPERATOR_EMAIL:-admin@lewik.cl}"
OPERATOR_PASSWORD="${OPERATOR_PASSWORD:-cambiar-esto-ya-1234}"
OUT="${OUT:-scripts/e2e-seed.json}"
RESET=1
[ "${1:-}" = "--no-reset" ] && RESET=0

# --- constantes del dataset (el front puede hardcodearlas) -------------------
PASS="Password.E2E.2026"
SLUG="e2e"; SLUG_B="e2e-otra"; SLUG_SUSP="e2e-suspendida"
OWNER="owner@e2e.test"; STAFF="staff@e2e.test"; CUSTOMER="cliente@e2e.test"

BODY="$(mktemp)"; JP="$BODY.p"; JA="$BODY.a"; JB="$BODY.b"
touch "$JP" "$JA" "$JB"
trap 'rm -f "$BODY" "$BODY".*' EXIT

if [ -t 1 ]; then G=$'\e[32m'; R=$'\e[31m'; C=$'\e[36m'; Z=$'\e[0m'; else G=; R=; C=; Z=; fi
step() { printf "${C}» %s${Z}\n" "$1"; }
die()  { printf "${R}✗ %s${Z}\n" "$1"; [ -s "$BODY" ] && head -c 300 "$BODY"; echo; exit 1; }
j()    { jq -r "$1" "$BODY" 2>/dev/null; }

command -v jq >/dev/null 2>&1 || die "jq no está instalado."
curl -s -o /dev/null --max-time 5 "$API/health/db" || die "La API no responde en $API"

# ---- helpers HTTP -----------------------------------------------------------
# El host resuelve la tienda, igual que en producción. Ningún X-Tenant-Id: si algo
# necesitara el header, este script no probaría el camino que el front va a usar.
# El cuerpo va por STDIN, no por -d. En Git Bash sobre Windows un argumento con caracteres no
# ASCII se transcodifica antes de que curl lo vea, y llega al servidor como UTF-8 inválido:
# "Colección" en el nombre de un producto devuelve 400 request.invalid_body con -d, y 200 por
# stdin. Los mismos bytes, distinto camino. Como el catálogo de una tienda chilena tiene acentos
# por definición, esto no es un detalle que se pueda esquivar escribiendo sin tildes.
call() { # MÉTODO PATH HOST [json] [jar] -> status en stdout, body en $BODY
  local m="$1" p="$2" h="$3" json="${4:-}" jar="${5-}"
  local -a a=(-s -o "$BODY" -w '%{http_code}' -X "$m" "$API$p" -H "X-Requested-With: LeWiKPanel")
  [ -n "$h" ]    && a+=(-H "Host: $h")
  [ -n "$jar" ]  && a+=(-b "$jar" -c "$jar")
  if [ -n "$json" ]; then
    printf '%s' "$json" | curl "${a[@]}" -H "Content-Type: application/json" --data-binary @-
  else
    curl "${a[@]}"
  fi
}
plat()  { call "$1" "$2" "" "${3:-}" "$JP"; }                       # operador LeWiK
adm()   { call "$1" "$2" "$SLUG.localhost" "${3:-}" "$JA"; }        # staff de la tienda e2e
admb()  { call "$1" "$2" "$SLUG_B.localhost" "${3:-}" "$JB"; }      # staff de la tienda B
anon()  { call "$1" "$2" "${3:-$SLUG.localhost}" "${4:-}" ""; }     # comprador, sin sesión

need() { [ "$1" = "$2" ] || die "$3 (HTTP $1)"; }

# =============================================================================
step "Login de plataforma"
st=$(plat POST /auth/platform/login "{\"email\":\"$OPERATOR_EMAIL\",\"password\":\"$OPERATOR_PASSWORD\"}")
[ "$st" = "200" ] || die "No pude loguear al operador de plataforma. Sembralo con Platform:SeedOperator* o pasá OPERATOR_EMAIL/OPERATOR_PASSWORD."

# =============================================================================
if [ "$RESET" = "1" ]; then
  step "Reset: borrando lo sembrado antes"
  # -i es obligatorio: sin él docker exec no engancha stdin y el heredoc de abajo se pierde
  # entero, dejando el borrado como un no-op silencioso.
  PSQL="docker exec -i lewik_store_db psql -U lewik -d lewik_store -q -v ON_ERROR_STOP=1"
  $PSQL -c "SELECT 1;" >/dev/null 2>&1 || die "No alcancé Postgres por 'docker exec lewik_store_db psql'. Usá --no-reset si la base ya está limpia."
  # Solo cascadean staff_users, order_lines, stock_movements, product_* y customer_sessions;
  # el resto se borra a mano. El WHERE está anclado al prefijo 'e2e' y a nada más: este script
  # no puede tocar datos que no haya creado él.
  $PSQL <<'SQL' >/dev/null 2>&1
BEGIN;
CREATE TEMP TABLE _t AS SELECT id FROM stores WHERE slug IN ('e2e','e2e-otra','e2e-suspendida');
DELETE FROM variant_option_values WHERE product_variant_id IN
  (SELECT id FROM product_variants WHERE tenant_id IN (SELECT id FROM _t));
DELETE FROM refunds               WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM payments              WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM orders                WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM inventories           WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM preorders             WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM purchase_limits       WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM payment_method_configs WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM customer_sessions     WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM customers             WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM staff_sessions        WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM products              WHERE tenant_id IN (SELECT id FROM _t);
DELETE FROM stores                WHERE id IN (SELECT id FROM _t);
COMMIT;
SQL
  [ $? -eq 0 ] || die "El borrado falló; la base quedó como estaba."

  # StoreResolver cachea host -> tienda en Redis por 5 minutos y solo invalida cuando la tienda
  # cambia POR LA API. Un DELETE directo en la base la deja apuntando a un tenant que ya no
  # existe, y el síntoma es desconcertante: el host resuelve, pero después todo contesta
  # platform.store_not_found. Se limpian todas las entradas de host en vez de calcular las
  # grafías (slug.localhost, www.slug.localhost, panel.…): errar una sola reproduce el bug.
  docker exec lewik_store_redis sh -c \
    "redis-cli --scan --pattern 'tenant:host:*' | xargs -r redis-cli DEL" >/dev/null 2>&1 \
    || echo "  (aviso: no pude limpiar la caché de Redis; si algo responde platform.store_not_found, esperá 5 minutos)"
fi

# =============================================================================
step "Tiendas"
# Igual que checkout(): resultado en una global, no por stdout. Llamar a estos helpers dentro de
# $( ) los corre en un subshell, y ahí `die` solo mata el subshell — el script seguiría de largo
# con un id vacío y reventaría cinco pasos después con un 404 que no explica nada. Pasó: con
# --no-reset, mkstore moría por platform.slug_taken y la corrida continuaba igual.
LAST_STORE=""
mkstore() { # slug nombre email
  local st
  st=$(plat POST /platform/stores \
    "{\"name\":\"$2\",\"slug\":\"$1\",\"ownerEmail\":\"$3\",\"ownerName\":\"Owner E2E\",\"ownerPassword\":\"$PASS\"}")
  [ "$st" = "200" ] || die "No se pudo crear la tienda '$1' — $(j '.title // empty') (HTTP $st). Con --no-reset esto pasa si ya la sembraste: corré sin la opción."
  LAST_STORE="$(j '.id')"
}
mkstore "$SLUG"      "Tienda E2E"        "$OWNER";                T="$LAST_STORE"
mkstore "$SLUG_B"    "Tienda E2E Otra"   "owner@e2e-otra.test";   TB="$LAST_STORE"
mkstore "$SLUG_SUSP" "Tienda Suspendida" "owner@e2e-susp.test";   TS="$LAST_STORE"

st=$(call POST /auth/staff/login "$SLUG.localhost"      "{\"email\":\"$OWNER\",\"password\":\"$PASS\"}" "$JA")
need "$st" 200 "login del owner de $SLUG"
st=$(call POST /auth/staff/login "$SLUG_B.localhost" "{\"email\":\"owner@e2e-otra.test\",\"password\":\"$PASS\"}" "$JB")
need "$st" 200 "login del owner de $SLUG_B"

# Un segundo usuario con rol Staff: el front necesita comprobar que la UI se recorta por rol
# (y que el backend lo rechaza igual, que es lo que de verdad protege).
adm POST /admin/staff "{\"email\":\"$STAFF\",\"name\":\"Staff E2E\",\"password\":\"$PASS\",\"role\":\"Staff\"}" >/dev/null

# Transferencia configurada: sin esto, initiate devuelve 409 payment.gateway_not_configured y
# el front se choca con una pared que solo el Owner puede destrabar.
adm PUT /admin/payment-methods/Transfer \
  '{"credentialsJson":"{\"banco\":\"Banco de Chile\",\"tipoCuenta\":\"Corriente\",\"numero\":\"00-123-45678-90\",\"titular\":\"Tienda E2E SpA\",\"rut\":\"77.123.456-7\",\"email\":\"pagos@e2e.test\"}"}' >/dev/null

# =============================================================================
step "Catálogo"
LAST_PRODUCT=""; LAST_VARIANT=""
mkproduct() { # sku nombre precio
  local st
  st=$(adm POST /admin/products "{\"sku\":\"$1\",\"name\":\"$2\",\"price\":$3,\"currency\":\"CLP\"}")
  [ "$st" = "200" ] || die "No se pudo crear el producto '$1' — $(j '.title // empty') (HTTP $st)"
  LAST_PRODUCT="$(j '.')"
  adm GET "/products/$LAST_PRODUCT" >/dev/null
  LAST_VARIANT="$(j '.variants[0].id')"
  [ -n "$LAST_VARIANT" ] && [ "$LAST_VARIANT" != "null" ] || die "El producto '$1' quedó sin variante"
}
addstock() { adm POST "/admin/variants/$1/stock" "{\"quantity\":$2,\"reason\":\"seed e2e\"}" >/dev/null; }

mkproduct E2E-BOX      'Booster Box Scarlet & Violet' 89990; P_BOX="$LAST_PRODUCT"; V_BOX="$LAST_VARIANT"
mkproduct E2E-AGOTADO  'Elite Trainer Box (agotada)'  54990; P_OUT="$LAST_PRODUCT"; V_OUT="$LAST_VARIANT"
mkproduct E2E-LIMITADO 'Alt Art Charizard'           129990; P_LIM="$LAST_PRODUCT"; V_LIM="$LAST_VARIANT"
addstock "$V_BOX" 50
addstock "$V_LIM" 100
# E2E-AGOTADO se agota de verdad: se cargan 2 unidades y alguien se las lleva. Queda con
# available 0 y reserved 2, que es cómo se ve un producto vendido — no lo mismo que uno sin
# inventario, que ni siquiera tiene fila y responde order.no_stock en vez de
# inventory.insufficient_stock.
addstock "$V_OUT" 2
anon POST /orders "$SLUG.localhost" \
  "{\"customer\":{\"email\":\"agoto@e2e.test\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$V_OUT\",\"quantity\":2}]}" >/dev/null

# Topes anti-scalping. maxPerOrder viaja a la vidriera; maxPerCustomer NO, y se aplica igual:
# es el 409 que el front tiene que manejar aunque su selector lo haya permitido.
adm PUT "/admin/variants/$V_LIM/purchase-limit" '{"maxPerOrder":2,"maxPerCustomer":3,"windowDays":30}' >/dev/null

# Producto con dos ejes: 4 combinaciones, una sin stock, para el selector de opciones.
adm POST /admin/products/with-options '{
  "name": "Blister Paldea Evolved",
  "description": "Blister con carta promo",
  "options": [
    { "name": "Carta",  "values": ["Charizard", "Meganium"] },
    { "name": "Idioma", "values": ["Inglés", "Español"] }
  ],
  "variants": [
    { "sku":"E2E-BLI-CHAR-EN","price":12990,"currency":"CLP","selections":{"Carta":"Charizard","Idioma":"Inglés"} },
    { "sku":"E2E-BLI-CHAR-ES","price":12990,"currency":"CLP","selections":{"Carta":"Charizard","Idioma":"Español"} },
    { "sku":"E2E-BLI-MEGA-EN","price":11990,"currency":"CLP","selections":{"Carta":"Meganium","Idioma":"Inglés"} },
    { "sku":"E2E-BLI-MEGA-ES","price":11990,"currency":"CLP","selections":{"Carta":"Meganium","Idioma":"Español"} }
  ]}' >/dev/null
P_BLI="$(j '.')"
adm GET "/products/$P_BLI" >/dev/null
V_BLI_CHAR_EN="$(j '.variants[] | select(.sku=="E2E-BLI-CHAR-EN") | .id')"
V_BLI_CHAR_ES="$(j '.variants[] | select(.sku=="E2E-BLI-CHAR-ES") | .id')"
V_BLI_MEGA_EN="$(j '.variants[] | select(.sku=="E2E-BLI-MEGA-EN") | .id')"
V_BLI_MEGA_ES="$(j '.variants[] | select(.sku=="E2E-BLI-MEGA-ES") | .id')"
addstock "$V_BLI_CHAR_EN" 20
addstock "$V_BLI_CHAR_ES" 15
addstock "$V_BLI_MEGA_EN" 8
# MEGA-ES queda sin inventario: combinación válida del selector, no vendible.
adm PUT "/admin/products/$P_BLI/purchase-limit" '{"maxPerOrder":5}' >/dev/null

# =============================================================================
step "Preventas"
mkdrop() { adm PUT "/admin/variants/$1/preorder" \
  "{\"capacity\":$2,\"releaseDate\":\"$3\",\"depositType\":\"Percentage\",\"depositValue\":$4}" >/dev/null; }

mkproduct E2E-DROP 'Drop: Colección 151 sellada' 149990; P_DROP="$LAST_PRODUCT"; P_DROP_V="$LAST_VARIANT"
mkdrop "$P_DROP_V" 100 "2026-12-01T00:00:00Z" 30

# Drop casi agotado: queda 1 cupo. Sirve para ver el contador llegar a cero en vivo y el
# 409 preorder.capacity_exceeded del siguiente comprador.
mkproduct E2E-DROP-CASI 'Drop: última caja' 199990; P_CASI="$LAST_PRODUCT"; P_CASI_V="$LAST_VARIANT"
mkdrop "$P_CASI_V" 5 "2026-11-01T00:00:00Z" 50
anon POST /orders "$SLUG.localhost" \
  "{\"customer\":{\"email\":\"otro@e2e.test\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$P_CASI_V\",\"quantity\":4}]}" >/dev/null

# Drop ya cerrado, con el ciclo completo recorrido: vendido, pagado, mercadería cargada,
# liberado y cerrado. La variante volvió a venderse por stock — el estado en que queda una
# preventa después del evento.
mkproduct E2E-DROP-CERRADO 'Drop cerrado: Obsidian Flames' 79990; P_CERR="$LAST_PRODUCT"; P_CERR_V="$LAST_VARIANT"
mkdrop "$P_CERR_V" 10 "2026-06-01T00:00:00Z" 30
anon POST /orders "$SLUG.localhost" \
  "{\"customer\":{\"email\":\"early@e2e.test\",\"phone\":\"+56911111111\"},\"items\":[{\"productVariantId\":\"$P_CERR_V\",\"quantity\":2}]}" >/dev/null
O_CERR="$(j '.orderId')"
adm POST "/admin/orders/$O_CERR/payments" '{"amount":159980}' >/dev/null
addstock "$P_CERR_V" 30
adm POST "/admin/orders/$O_CERR/release" >/dev/null
st=$(adm POST "/admin/variants/$P_CERR_V/preorder/close")
need "$st" 200 "cerrar el drop de E2E-DROP-CERRADO"

# =============================================================================
step "Pedidos en cada estado"
# Deja el resultado en globales en vez de imprimirlo: llamarla dentro de $( ) la correría en un
# subshell y el accessToken se perdería al volver.
LAST_ORDER=""; LAST_TOKEN=""
checkout() { # email variantId qty
  anon POST /orders "$SLUG.localhost" \
    "{\"customer\":{\"email\":\"$1\",\"phone\":\"+56922222222\"},\"items\":[{\"productVariantId\":\"$2\",\"quantity\":$3}]}" >/dev/null
  LAST_ORDER="$(j '.orderId')"; LAST_TOKEN="$(j '.accessToken')"
  [ -n "$LAST_ORDER" ] && [ "$LAST_ORDER" != "null" ] || die "checkout de $1 falló"
}

# 1. Sin pagar, con el reloj de la reserva corriendo y su token vivo.
checkout "pendiente@e2e.test" "$V_BOX" 2
O_PENDIENTE="$LAST_ORDER"; TOK_PENDIENTE="$LAST_TOKEN"
anon GET "/pay/$TOK_PENDIENTE" "$SLUG.localhost" >/dev/null
EXPIRA="$(j '.reservationExpiresAt')"

# 2. Preventa con el abono pagado -> AwaitingRelease.
checkout "abonado@e2e.test" "$P_DROP_V" 2; O_ABONADO="$LAST_ORDER"
adm POST "/admin/orders/$O_ABONADO/payments" '{"amount":89994}' >/dev/null

# 3. Pagado entero -> Paid.
checkout "pagado@e2e.test" "$V_BOX" 1; O_PAGADO="$LAST_ORDER"
adm POST "/admin/orders/$O_PAGADO/payments" '{"amount":89990}' >/dev/null

# 4. Entregado.
checkout "entregado@e2e.test" "$V_BOX" 1; O_ENTREGADO="$LAST_ORDER"
adm POST "/admin/orders/$O_ENTREGADO/payments" '{"amount":89990}' >/dev/null
adm POST "/admin/orders/$O_ENTREGADO/prepare" >/dev/null
adm GET "/admin/orders/$O_ENTREGADO" >/dev/null; L="$(j '.lines[0].id')"
adm POST "/admin/orders/$O_ENTREGADO/lines/$L/fulfill" '{"quantity":1}' >/dev/null

# 5. Cancelado.
checkout "cancelado@e2e.test" "$V_BOX" 1; O_CANCELADO="$LAST_ORDER"
adm POST "/admin/orders/$O_CANCELADO/cancel" >/dev/null

# 6. Con link de pago generado por la tienda. El link se muestra UNA sola vez —
# se guarda solo su hash—, así que este es el único momento en que existe el token.
checkout "link@e2e.test" "$V_BOX" 1; O_LINK="$LAST_ORDER"
adm POST "/admin/orders/$O_LINK/payment-link" '{"validForDays":30}' >/dev/null
LINK_URL="$(j '.url')"; TOK_LINK="${LINK_URL##*/}"

# =============================================================================
step "Cliente registrado, con historial de invitado"
# Compra primero como invitado y DESPUÉS se registra con el mismo email: la cuenta nace con
# esa compra adentro. Es el caso que vale la pena tener sembrado.
checkout "$CUSTOMER" "$V_BLI_CHAR_EN" 1; O_HISTORIAL="$LAST_ORDER"
st=$(anon POST /auth/customer/register "$SLUG.localhost" \
  "{\"email\":\"$CUSTOMER\",\"password\":\"$PASS\",\"phone\":\"+56933333333\",\"name\":\"Cliente E2E\"}")
need "$st" 200 "registro del cliente"

# La promoción es todo el punto de este fixture: si el pedido de invitado no quedó dentro de la
# cuenta, el dato sembrado no sirve para el test que iba a usarlo.
JC="$BODY.c"; touch "$JC"
call POST /auth/customer/login "$SLUG.localhost" "{\"email\":\"$CUSTOMER\",\"password\":\"$PASS\"}" "$JC" >/dev/null
call GET /account/orders "$SLUG.localhost" "" "$JC" >/dev/null
j "[.[] | select(.id==\"$O_HISTORIAL\")] | length" | grep -q '^1$' \
  || die "el pedido de invitado no quedó en la cuenta del cliente"

# =============================================================================
step "Tienda B (aislamiento) y tienda suspendida"
admb POST /admin/products '{"sku":"OTRA-BOX","name":"Producto de otra tienda","price":10000,"currency":"CLP"}' >/dev/null
P_OTRA="$(j '.')"; admb GET "/products/$P_OTRA" >/dev/null; V_OTRA="$(j '.variants[0].id')"
admb POST "/admin/variants/$V_OTRA/stock" '{"quantity":10,"reason":"seed"}' >/dev/null
plat POST "/platform/stores/$TS/suspend" >/dev/null

# =============================================================================
step "Verificando lo sembrado"
st=$(anon GET /storefront "$SLUG.localhost"); need "$st" 200 "GET /storefront"
NPROD="$(j '.products | length')"

# Los números salen de la API, no de lo que este script cree haber hecho. Escribirlos a mano
# es cómo un manifiesto se desincroniza en silencio: el pedido con abono consume 2 cupos del
# drop principal, y una constante "100" habría mentido desde la primera corrida.
av() { j "[.products[].variants[] | select(.id==\"$1\")][0].availability.$2"; }
AV_BOX="$(av "$V_BOX" available)"
AV_OUT="$(av "$V_OUT" available)"
AV_LIM="$(av "$V_LIM" available)"
AV_DROP="$(av "$P_DROP_V" available)"
AV_CASI="$(av "$P_CASI_V" available)"
AV_CERR="$(av "$P_CERR_V" available)"
K_DROP="$(av "$P_DROP_V" kind)"; K_CERR="$(av "$P_CERR_V" kind)"
AV_BLI_1="$(av "$V_BLI_CHAR_EN" available)"; AV_BLI_2="$(av "$V_BLI_CHAR_ES" available)"
AV_BLI_3="$(av "$V_BLI_MEGA_EN" available)"; AV_BLI_4="$(av "$V_BLI_MEGA_ES" available)"

# Lo que sí se afirma son las invariantes de las que dependen los tests del front: qué forma de
# venta tiene cada variante y si se puede comprar. Los números son datos; esto es el contrato.
[ "$K_DROP" = "Preorder" ] || die "el drop principal debería leerse Preorder, se lee $K_DROP"
[ "$K_CERR" = "Stock" ]    || die "el drop cerrado debería haber vuelto a Stock, se lee $K_CERR"
[ "$AV_CASI" = "1" ]       || die "el drop casi agotado debería tener 1 cupo, tiene $AV_CASI"
[ "$AV_OUT" = "0" ]        || die "la variante agotada debería tener 0, tiene $AV_OUT"
[ "$AV_BLI_4" = "0" ]      || die "la combinación no vendible debería tener 0, tiene $AV_BLI_4"
[ "$(j "[.products[].variants[] | select(.id==\"$V_OUT\")][0].availability.isSellable")" = "false" ] \
  || die "la variante agotada no debería ser vendible"
[ "$(j "[.products[].variants[] | select(.id==\"$V_LIM\")][0].limit.maxPerCustomer")" = "null" ] \
  || die "el tope por cliente no debería publicarse en la vidriera"

st=$(anon POST "/orders/$O_PENDIENTE/payments/initiate" "$SLUG.localhost" '{"gateway":"Transfer","type":"Full"}')
need "$st" 200 "initiate con transferencia (¿quedó configurada la pasarela?)"

# La tienda B y la suspendida, también verificadas: son la base de los tests de aislamiento.
st=$(anon GET /storefront "$SLUG_SUSP.localhost")
[ "$st" = "403" ] || die "la tienda suspendida debería devolver 403, devolvió $st"
st=$(anon GET /storefront "$SLUG_B.localhost")
need "$st" 200 "la tienda B debería atender"

# =============================================================================
mkdir -p "$(dirname "$OUT")"
cat > "$OUT" <<JSON
{
  "generatedAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "api": "$API",
  "password": "$PASS",
  "stores": {
    "main":      { "slug": "$SLUG",      "tenantId": "$T",  "host": "$SLUG.localhost",      "origin": "$API" },
    "other":     { "slug": "$SLUG_B",    "tenantId": "$TB", "host": "$SLUG_B.localhost" },
    "suspended": { "slug": "$SLUG_SUSP", "tenantId": "$TS", "host": "$SLUG_SUSP.localhost" }
  },
  "users": {
    "platformOperator": { "email": "$OPERATOR_EMAIL", "password": "$OPERATOR_PASSWORD" },
    "owner":            { "email": "$OWNER",    "password": "$PASS", "role": "Owner" },
    "staff":            { "email": "$STAFF",    "password": "$PASS", "role": "Staff" },
    "customer":         { "email": "$CUSTOMER", "password": "$PASS", "hasGuestHistory": true }
  },
  "variants": {
    "inStock":        { "id": "$V_BOX", "sku": "E2E-BOX",      "price": 89990,  "kind": "Stock", "available": $AV_BOX },
    "outOfStock":     { "id": "$V_OUT", "sku": "E2E-AGOTADO",  "price": 54990,  "kind": "Stock", "available": $AV_OUT,
                        "note": "vendido, no inexistente: tiene fila de inventario, así que un checkout da inventory.insufficient_stock" },
    "limited":        { "id": "$V_LIM", "sku": "E2E-LIMITADO", "price": 129990, "kind": "Stock", "available": $AV_LIM,
                        "maxPerOrder": 2, "maxPerCustomerHidden": 3, "windowDaysHidden": 30,
                        "note": "el tope por cliente NO viaja a /storefront y se aplica igual: 3 unidades en 30 días" },
    "drop":           { "id": "$P_DROP_V", "sku": "E2E-DROP",         "price": 149990, "kind": "Preorder", "capacity": 100, "available": $AV_DROP, "depositPercent": 30 },
    "dropAlmostGone": { "id": "$P_CASI_V", "sku": "E2E-DROP-CASI",    "price": 199990, "kind": "Preorder", "capacity": 5,   "available": $AV_CASI, "depositPercent": 50,
                        "note": "queda 1: el siguiente comprador de 2 recibe preorder.capacity_exceeded" },
    "dropClosed":     { "id": "$P_CERR_V", "sku": "E2E-DROP-CERRADO", "price": 79990,  "kind": "Stock",    "available": $AV_CERR,
                        "note": "fue drop, se liberó y se cerró; ahora vende por stock" },
    "otherStore":     { "id": "$V_OTRA", "sku": "OTRA-BOX", "note": "pertenece a $SLUG_B; usarla contra $SLUG debe dar 404" }
  },
  "productWithOptions": {
    "id": "$P_BLI",
    "maxPerOrder": 5,
    "variants": {
      "charizardEn": { "id": "$V_BLI_CHAR_EN", "sku": "E2E-BLI-CHAR-EN", "price": 12990, "available": $AV_BLI_1 },
      "charizardEs": { "id": "$V_BLI_CHAR_ES", "sku": "E2E-BLI-CHAR-ES", "price": 12990, "available": $AV_BLI_2 },
      "meganiumEn":  { "id": "$V_BLI_MEGA_EN", "sku": "E2E-BLI-MEGA-EN", "price": 11990, "available": $AV_BLI_3 },
      "meganiumEs":  { "id": "$V_BLI_MEGA_ES", "sku": "E2E-BLI-MEGA-ES", "price": 11990, "available": $AV_BLI_4,
                       "note": "combinación válida del selector, no vendible" }
    }
  },
  "orders": {
    "pendingPayment": { "id": "$O_PENDIENTE", "accessToken": "$TOK_PENDIENTE", "reservationExpiresAt": "$EXPIRA",
                        "fulfillmentStatus": "PendingPayment", "paymentStatus": "Pending",
                        "note": "el reloj corre: se cancela solo a los 30 minutos" },
    "awaitingRelease":{ "id": "$O_ABONADO",   "fulfillmentStatus": "AwaitingRelease", "paymentStatus": "Deposited" },
    "paid":           { "id": "$O_PAGADO",    "fulfillmentStatus": "Paid",       "paymentStatus": "Paid" },
    "delivered":      { "id": "$O_ENTREGADO", "fulfillmentStatus": "Delivered",  "paymentStatus": "Paid" },
    "cancelled":      { "id": "$O_CANCELADO", "fulfillmentStatus": "Cancelled",  "paymentStatus": "Pending" },
    "withPaymentLink":{ "id": "$O_LINK", "token": "$TOK_LINK", "note": "el link se muestra una sola vez; este es ese momento" },
    "customerHistory":{ "id": "$O_HISTORIAL", "note": "hecho como invitado antes de registrarse; debe aparecer en /account/orders" }
  }
}
JSON

printf "\n${G}✓ Sembrado.${Z} Manifiesto en ${C}%s${Z}\n\n" "$OUT"
cat <<EOF
  Vidriera      $API/storefront          (Host: $SLUG.localhost)
  Contador      http://$SLUG.localhost:${API##*:}/signalr-test.html
  Panel         owner  $OWNER / $PASS
                staff  $STAFF / $PASS
  Comprador     $CUSTOMER / $PASS   (con historial de invitado)

  $NPROD productos · drop con $AV_DROP/100 cupos · otro con $AV_CASI · uno ya cerrado ($AV_CERR en stock)
  7 pedidos, uno por estado, y un token de invitado con el reloj corriendo

  Ojo: el pedido 'pendingPayment' se cancela solo a los 30 minutos (Reservations:TtlMinutes).
  Si tu suite lo necesita vivo, volvé a correr este script antes de la tanda.
EOF
