# LeWiK Store API · Guía para Frontend

> **Estado: fin de Fase 4.7.** Cubre catálogo, inventario, preventas (con su ciclo de vida
> completo), límites anti-scalping, ciclo completo del pedido, pagos (transferencia + Webpay +
> Mercado Pago), reembolsos, autenticación de las tres poblaciones, resolución de tienda por
> dominio, vencimiento de reservas y disponibilidad en tiempo real.
>
> **Esta versión rompe el contrato anterior.** Si venís de la guía previa, leé primero §1.

---

## 0 · Cambios desde el borrador anterior de esta guía

Cuatro cosas cambiaron después de que se escribió la versión anterior. Las tres primeras
afectan código del front.

| Qué | Antes | Ahora |
|---|---|---|
| **Límites públicos** | `/storefront` publicaba `maxPerOrder`, `maxPerCustomer` y `windowDays` | Solo **`maxPerOrder`**. Los topes por cliente existen y se aplican, pero no se anuncian (§5) |
| **Tienda suspendida** | Todo `/auth/*` seguía abierto | El **login de clientes también corta** con 403. Solo siguen abiertos `/auth/staff/*`, `/auth/platform/*` y `/health/*` (§2) |
| **Fin de una preventa** | No existía: una variante que era drop lo era para siempre | **`POST /admin/variants/{id}/preorder/close`** la devuelve a venta por stock (§11, §14-B) |
| **Contador en vivo** | El frame reflejaba el evento que lo disparó | Refleja **cómo se vende la variante ahora**: cargar stock de un drop abierto ya no cambia `kind` de `Preorder` a `Stock` (§6) |

---

## 1 · Qué cambió respecto de la versión anterior

| Antes | Ahora |
|---|---|
| Tienda por header `X-Tenant-Id` | **Por dominio** (`cardshop.lewik.app`, `www.cardshop.cl`). El header sobrevive solo en dev |
| Todo abierto sin auth | **Tres poblaciones autenticadas**; gestión bajo `/admin` y `/platform` |
| `POST /products`, `POST /variants/{id}/stock`, etc. | Movidos a **`/admin/...`** |
| `GET /orders/{id}` público | **Admin**. El invitado usa `/pay/{accessToken}` |
| Checkout devolvía un GUID | Devuelve `{ orderId, accessToken, reservationExpiresAt }` |
| La reserva de stock no vencía | **Vence** (TTL); el pedido impago se cancela solo |
| Catálogo: N+1 llamadas | **`GET /storefront`** trae todo en una |
| Sin tiempo real | **SignalR**: disponibilidad en vivo |

Checklist de migración del front:

1. Sacar `X-Tenant-Id` de las llamadas (en dev podés dejarlo, ver §2).
2. `credentials: 'include'` en **todas** las llamadas (las cookies son la sesión).
3. Header `X-Requested-With: LeWiKPanel` en todo POST/PUT/DELETE del panel y plataforma.
4. Prefijar con `/admin` las llamadas de gestión.
5. Guardar el `accessToken` del checkout: es la única llave del invitado a su pedido.
6. Mostrar cuenta regresiva con `reservationExpiresAt`.
7. Manejar `409 order.limit_per_customer` aunque el selector de cantidad lo haya permitido (§5).

---

## 2 · Setup

### Levantar el entorno

```bash
git clone <repo> && cd store-backend-core
docker compose --profile full up -d --build
```

API en `http://localhost:5223`, con Postgres y Redis. Migraciones aplicadas al arrancar.

### Cómo se identifica la tienda

Por el **host de la request a la API**, contra la tabla de tiendas:

| Host | Resuelve |
|---|---|
| `cardshop.lewik.app` | por slug |
| `panel.cardshop.lewik.app` | por slug (el prefijo `panel.` se ignora) |
| `cardshop.cl` / `www.cardshop.cl` | por dominio propio |
| `panel.cardshop.cl` | por dominio propio |

`panel.` es **routing del front**, no del backend: resuelve la misma tienda. Quién puede hacer
qué lo deciden los permisos, no el subdominio.

> **La trampa número uno en desarrollo.** Lo que importa es el host de la request **a la API**, no
> dónde vive el front. Si el dev-server está en `localhost:4200` y llamás a
> `http://localhost:5223/storefront`, el host es `localhost`, que no es ninguna tienda → **404
> `platform.store_not_found`**. Dos salidas: apuntar a `http://<slug>.localhost:5223` (el camino
> real, recomendado desde el día uno) o mandar `X-Tenant-Id` mientras estés en dev.

**En desarrollo:** `*.localhost` resuelve a 127.0.0.1 sin tocar `/etc/hosts`. Con el dev-server
en `cardshop.localhost:4200` y proxy a la API, todo funciona.

**Alternativa dev:** el header `X-Tenant-Id: <guid>` sigue funcionando (config
`Tenancy:AllowHeaderOverride`). **En producción está deshabilitado y la app no arranca si alguien
lo habilita.** No construyas nada que dependa de él.

**Host desconocido** → no hay tienda: `/storefront` devuelve `404 platform.store_not_found`, las
demás lecturas vuelven vacías y las escrituras dan 400. Falla cerrado a propósito: una vidriera
vacía se leería como una tienda sin nada que vender, y es otra cosa.

**Tienda suspendida** → `403 platform.store_suspended` en todo, con tres excepciones:
`/auth/staff/*`, `/auth/platform/*` y `/health/*`. El staff llega al login y le explican el
motivo; **el comprador no entra**, ni siquiera a loguearse: una tienda suspendida está cerrada
al público.

### Cookies y CORS

- **Todas las llamadas** llevan `credentials: 'include'`. La sesión viaja en cookie `HttpOnly`
  — el JS no la ve ni la necesita.
- Las cookies son **host-only**: `panel.tienda.cl` y `www.tienda.cl` no comparten sesión. Es
  deliberado.
- CORS acepta solo orígenes de la lista `Cors:AllowedOrigins` (en dev: `http://localhost:4200`).
  Si el front corre en otro puerto, hay que agregarlo — no hay comodín posible, porque
  `AllowCredentials` lo prohíbe.

> Si el login responde 200 pero la llamada siguiente vuelve 401, mirá primero el `SameSite` de la
> cookie entre el host del front y el host de la API. No está verificado para la combinación
> `localhost:4200` → `<slug>.localhost:5223`; si aparece, avisá y lo resolvemos con datos.

### Header anti-CSRF

Todo **POST/PUT/DELETE** bajo `/admin` y `/platform` debe llevar:

```
X-Requested-With: LeWiKPanel
```

Sin él → `403 request.csrf_header_missing`. Los GET no lo necesitan. El storefront público
tampoco.

### Convenciones JSON

`camelCase`; enums como string (`"Percentage"`, `"Paid"`); fechas ISO 8601 **UTC**; montos como
número sin decimales sobrantes (`150000`, no `150000.0000`). CLP se maneja en enteros.

---

## 3 · Errores

**La clave estable es `title`** (código machine-readable). `detail` es texto humano en inglés —
localizá en el front usando `title` como llave.

```json
{
  "title": "inventory.insufficient_stock",
  "status": 409,
  "detail": "Requested 5 but only 3 available.",
  "traceId": "00-..."
}
```

| Status | Significado |
|---|---|
| 400 | Validación, body ilegible (`request.invalid_body`), sin tenant |
| 401 | Sin sesión, sesión vencida o revocada |
| 403 | Sesión válida sin permiso (rol, otra tienda, CSRF, tienda suspendida) |
| 404 | No existe (o no es visible para quien pregunta) |
| 409 | Conflicto de negocio (stock, límites, transiciones) |

**Errores de validación de forma** traen `errors` agrupado por campo:

```json
{ "title": "One or more validation errors occurred.", "status": 400,
  "errors": { "Name": ["'Name' must not be empty."] } }
```

Regla: si `status == 400` y hay `errors` → mostrar por campo; si no, usar `title`.

**401 vs 403:** con 401 mandá a login; con 403 mostrá "no tenés permiso" — reintentar no sirve.

---

## 4 · Autenticación

### El modelo, en simple

**No hay tokens que manejar.** El backend deja una cookie al hacer login y el navegador la manda
sola. El front no la ve, no la guarda y no la pone en ningún header.

Cinco reglas y no hay más:

1. **`credentials: 'include'` en todas las llamadas.** Es lo único que hay que acordarse de hacer.
   Si falta, el login devuelve 200 y **todo lo siguiente devuelve 401** — el error más confuso de
   diagnosticar y el más fácil de prevenir.
2. **Hay tres puertas, no una.** Comprador, staff y plataforma tienen cada uno su cookie y su
   área, y no se cruzan. Son tres aplicaciones que comparten dominio, no un login con roles.
3. **La tienda sale de la URL, no del front.** El backend la deduce del dominio de la request. El
   front **nunca** manda un id de tienda (ver §2).
4. **Para escribir en el panel, un header extra:** `X-Requested-With: LeWiKPanel` en todo
   POST/PUT/DELETE bajo `/admin` y `/platform`. Los GET no lo necesitan; el storefront tampoco.
5. **Para saber si hay sesión, preguntá.** La cookie es `HttpOnly` y el JS no la ve, a propósito.
   Al arrancar la app llamás a `/auth/staff/me` o `/account/me`: 200 hay, 401 no hay.

Las reglas 1 y 4 se resuelven de una vez en un interceptor y nadie más tiene que acordarse:

```ts
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const esPanel = /\/(admin|platform)\//.test(req.url);
  const escribe = req.method !== 'GET';
  return next(req.clone({
    withCredentials: true,
    setHeaders: esPanel && escribe ? { 'X-Requested-With': 'LeWiKPanel' } : {}
  }));
};
```

**Lo más importante para planificar:** la tienda pública **no necesita login para nada**. Ver el
catálogo, elegir, comprar, pagar y seguir el contador en vivo son todos anónimos. Se puede
construir y probar la vidriera entera antes de tocar una pantalla de login — eso es para "mi
cuenta" y para el panel, que son features aparte. El comprador sin cuenta recibe un `accessToken`
en el checkout, que es su llave para volver a ver y pagar su pedido (§9).

**Los dos errores:** **401** = no hay sesión (o venció, o la revocaron) → mandalo a login.
**403** = hay sesión pero no puede — otro rol, otra tienda, o falta el header de la regla 4;
reintentar no sirve y volver a loguearse tampoco.

### Las tres poblaciones

Están **selladas entre sí**: una cookie de staff nunca sirve en el área de cliente ni al revés.

| Población | Login | Cookie | Área |
|---|---|---|---|
| **Staff** (panel) | `/auth/staff/*` | `lewik_panel_session` | `/admin/*` |
| **Cliente** (storefront) | `/auth/customer/*` | `lewik_store_session` | `/account/*` |
| **Plataforma** (LeWiK) | `/auth/platform/*` | `lewik_platform_session` | `/platform/*` |

### Staff

`POST /auth/staff/login` → body `{ email, password }`, sobre el host de la tienda.

```json
{ "displayName": "Juan Pérez", "role": "Owner", "expiresAt": "2026-08-01T02:00:00Z" }
```

Sesión de **12 horas** (pensada para un turno). Errores: `400 auth.invalid_credentials` (mismo
error si el email no existe o la clave está mal — no revelamos cuál), `409
auth.account_disabled`, `409 platform.store_suspended`.

- `GET /auth/staff/me` → `{ id, name, role, tenantId }`. Útil al arrancar la app para saber si
  hay sesión viva.
- `POST /auth/staff/logout` → 204. Revocación **instantánea**.

**Roles y qué habilita cada uno:**

| Rol | Puede |
|---|---|
| `Owner` | Todo, **incluidas las credenciales de pasarelas** |
| `Admin` | Todo menos pasarelas; **gestiona usuarios** |
| `Staff` | Operación diaria: catálogo, stock, drops, pedidos, pagos |
| `Cashier` | Igual que Staff hoy; reservado para el POS (Fase 5) |

El front debería ocultar lo que el rol no permite — pero **el backend lo valida igual**: ocultar
no es proteger.

### Cliente

- `POST /auth/customer/register` → `{ email, password, phone, name? }`. Sesión de **30 días**.
  - Si ese email ya compró **como invitado**, se promueve la misma cuenta y **conserva su
    historial**.
  - Si ya tenía cuenta → `409 customer.already_registered`.
- `POST /auth/customer/login` → `{ email, password }`.
- `POST /auth/customer/logout` → 204.

Respuesta de ambos: `{ name, email, expiresAt }`. Con la tienda suspendida, ambos devuelven
`403 platform.store_suspended` antes de llegar al handler.

**No hay recuperación de contraseña todavía** (necesita envío de emails, pendiente). El invitado
no queda bloqueado: accede a su pedido por el `accessToken` del checkout.

> ⚠️ **El registro de clientes no puede salir a producción sin verificación de email.** Hoy nada
> prueba que quien se registra sea el dueño de la casilla, así que conocer un email que compró de
> invitado alcanza para reclamar ese historial. Con verificación obligatoria el agujero se cierra
> solo. Es una condición de habilitación del storefront, no deuda difusa.

### Plataforma

`POST /auth/platform/login` → `{ email, password }`. Sesión de **8 horas**.
`GET /auth/platform/me`, `POST /auth/platform/logout`.

---

## 5 · Storefront público

### GET `/storefront` — todo en una llamada

**Esta es la llamada principal del storefront.** Trae tienda + catálogo + disponibilidad +
límites; evita N+1.

```json
{
  "store": { "name": "Card Shop", "slug": "cardshop" },
  "products": [
    {
      "id": "guid",
      "name": "Blister Paldea Evolved",
      "description": "Blister con carta promo",
      "options": [
        { "name": "Carta", "values": [ { "id": "guid", "value": "Charizard" } ] },
        { "name": "Idioma", "values": [ { "id": "guid", "value": "Inglés" } ] }
      ],
      "variants": [
        {
          "id": "guid",
          "sku": "BLI-CHAR-EN",
          "label": "Charizard / Inglés",
          "price": 12990,
          "currency": "CLP",
          "optionValueIds": ["guid", "guid"],
          "availability": {
            "kind": "Preorder",
            "available": 87,
            "isSellable": true,
            "releaseDate": "2026-09-01T00:00:00Z",
            "depositType": "Percentage",
            "depositValue": 30
          },
          "limit": { "maxPerOrder": 1 }
        }
      ],
      "limit": { "maxPerOrder": 5 }
    }
  ]
}
```

Cómo leerlo:

- **`availability.kind`** decide toda la UI de esa variante:
  - `"Stock"` → venta normal. `available` = unidades. Botón "Comprar", se cobra el total.
  - `"Preorder"` → drop. `available` = **cupo restante**, más `releaseDate` y política de abono.
    Botón "Reservar", y mostrar que se paga un abono.
- **`isSellable`** ya combina disponibilidad y estado — usalo para habilitar el botón en vez de
  comparar números.
- **`depositType`**: `"None"` (paga todo), `"Percentage"` (`depositValue` = %),
  `"FixedPerUnit"` (`depositValue` = monto por unidad).
- **Selectores agrupados**: cruzá `variants[].optionValueIds` con `options[].values[].id`. El
  `label` (`"Charizard / Inglés"`) sirve como display.

**Sobre `kind`:** una variante con preventa **activa** vende por cupo; si no, vende del stock.
Es la misma precedencia que aplica el checkout, y a propósito: la página no puede ofrecer lo que
el checkout va a rechazar. Cargar mercadería en una variante con drop abierto **no** cambia el
`kind` — lo cambia cerrar el drop (§11). Una variante sin ninguno de los dos se lee como
`Stock 0`.

### Límites: lo que se publica y lo que no

`limit` viene a nivel de variante y de producto, y **ambos se aplican**. Públicamente trae
**solo `maxPerOrder`**, para capar el selector de cantidad. Ese número se descubre igual pidiendo
de más, así que publicarlo no regala nada.

`maxPerCustomer` y `windowDays` **no salen**: son la receta completa para un revendedor (cuántas
cuentas hacer, cada cuánto rotarlas) y viven solo en el panel.

> **Consecuencia para el front:** el tope por cliente se sigue aplicando aunque no se anuncie, así
> que un checkout puede fallar con **`409 order.limit_per_customer`** aunque tu selector haya
> permitido esa cantidad. **Hay que manejar ese 409**, no es un caso raro. Y una política que solo
> tenga tope por cliente llega con `maxPerOrder: null`: no capes nada y dejá que el checkout
> responda.

### Endpoints públicos sueltos

| Método | Ruta | Devuelve |
|---|---|---|
| GET | `/products` | Catálogo sin disponibilidad (preferí `/storefront`) |
| GET | `/products/{id}` | Un producto |
| GET | `/variants/{id}/stock` | `{ productVariantId, available, reserved }` |
| GET | `/variants/{id}/preorder` | Estado del drop (incluye drops ya cerrados: es el registro histórico, no lo que se vende hoy) |

---

## 6 · Disponibilidad en tiempo real (SignalR)

Para páginas de drop: el cupo baja en vivo mientras otros compran.

```js
import * as signalR from "@microsoft/signalr";

const conn = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/store")          // mismo host que la tienda
  .withAutomaticReconnect()
  .build();

conn.on("availabilityChanged", u => {
  // { variantId, kind: "Stock" | "Preorder", available, isSellable }
  actualizarContador(u.variantId, u.kind, u.available, u.isSellable);
});

await conn.start();

// Suscribirse devuelve el valor ACTUAL en la misma llamada: no hace falta
// combinarlo con /storefront ni cuidar el orden.
const actual = await conn.invoke("WatchVariant", variantId);
if (actual) actualizarContador(actual.variantId, actual.kind, actual.available, actual.isSellable);

// al salir de la página:
await conn.invoke("UnwatchVariant", variantId);
```

Notas:

- **`WatchVariant` devuelve el estado actual** además de suscribir. No leas por un lado y te
  suscribas por otro: lo que cambie en el medio se pierde y el contador queda viejo para siempre.
  El hub se une al grupo antes de consultar, así que lo peor que puede pasar es pintar el mismo
  número dos veces. Devuelve `null` si la tienda no se resolvió.
- **El payload dice siempre cómo se vende la variante *ahora***, no qué evento lo disparó. Cargar
  stock en un drop abierto sigue emitiendo `kind: "Preorder"` con el cupo; cerrar el drop emite
  el cambio de modo a `"Stock"`. Repintá `kind` con cada frame, no solo el número.
- La tienda se resuelve **del host de la conexión**; el cliente no la elige. Solo recibís
  novedades de la tienda que estás viendo.
- Se emite en compras, cancelaciones, vencimientos de reserva, reposiciones de stock, cambios de
  cupo, y apertura/cierre de un drop.
- Si la tienda está suspendida, la conexión se rechaza.
- **Tratá el tiempo real como mejora, no como fuente de verdad**: el estado inicial siempre viene
  de `/storefront` o del propio `WatchVariant`. Si SignalR no conecta, la página debe funcionar
  igual.

> **Para ver el hub andando sin front:** en Development la API sirve
> `http://<slug>.localhost:5223/signalr-test.html`. Dos pestañas con dos slugs distintos mirando
> el mismo `variantId` es la demo de aislamiento entre tiendas.

---

## 7 · Checkout

### POST `/orders` — público

Invitado:

```json
{
  "customer": { "email": "cliente@test.cl", "phone": "+56911111111", "name": "Cliente" },
  "items": [ { "productVariantId": "guid", "quantity": 2 } ]
}
```

Cliente logueado: **omitir `customer`** (sale de la sesión); mandar solo `items`.

**200** →

```json
{
  "orderId": "guid",
  "accessToken": "AbC123...",
  "reservationExpiresAt": "2026-07-30T14:35:00Z"
}
```

**Tres cosas que el front debe hacer con esto:**

1. **Guardar el `accessToken`.** Es la única llave del invitado a su pedido (ver/pagar).
   Guardalo y, si el comprador va a volver, mostrale el link `/pagar/{accessToken}`.
2. **Mostrar cuenta regresiva** con `reservationExpiresAt`: es el plazo para pagar antes de que
   la reserva se libere.
3. Mandar al comprador a pagar (§8).

| Error | Status | Código |
|---|---|---|
| Variante inexistente | 404 | `order.variant_not_found` |
| Sin inventario ni preventa | 409 | `order.no_stock` |
| Stock insuficiente | 409 | `inventory.insufficient_stock` |
| Cupo del drop agotado | 409 | `preorder.capacity_exceeded` |
| Drop cerrado | 409 | `preorder.closed` |
| Supera máx por pedido | 409 | `order.limit_per_order` |
| Supera máx por cliente (carrito + historial) | 409 | `order.limit_per_customer` |

> **Orden de evaluación:** los límites se chequean **antes** que el stock. Si el pedido excede
> ambos, vuelve el error de límite.

**No hay carrito persistente en el servidor, y es a propósito.** El carrito vive en el cliente;
la reserva ocurre al colocar el pedido. Reservar al agregar al carrito abriría un vector de
acaparamiento: en un drop, un revendedor llena carritos con todo el cupo, no paga nada, y bloquea
el stock hasta que expire — sin siquiera poner una tarjeta.

### Vencimiento de la reserva

Un pedido **sin ningún pago** libera su stock/cupo al vencer `reservationExpiresAt` y queda
`Cancelled`. Detalles que importan para la UI:

- **Iniciar un pago extiende el plazo** (para que no venza mientras el comprador está en la
  pasarela). Si mostrás el contador, refrescá `reservationExpiresAt` tras el `initiate`.
- **Cualquier pago recibido apaga el reloj** — aunque sea un abono parcial. El campo pasa a
  `null` y no vuelve.
- Si el comprador vuelve tarde: `/pay/{token}` mostrará el pedido cancelado, e `initiate`
  responderá **`409 order.cancelled`**. Hay que rehacer la compra.

---

## 8 · Pagos

Flujo: la tienda **configura** su pasarela (una vez), el comprador **inicia** el pago, y se
**confirma** (manual en transferencia, automático en las online).

### POST `/orders/{orderId}/payments/initiate` — público

```json
{ "gateway": "Transfer", "type": "Full" }
```

`gateway`: `Transfer` | `Webpay` | `MercadoPago`. `type`: `Full` (todo el saldo) | `Deposit` (el
abono de una preventa) | `Balance` (saldo restante). **El monto lo calcula el backend.**

**200** →

```json
{
  "paymentId": "guid",
  "gateway": "Webpay",
  "amount": 300000,
  "currency": "CLP",
  "redirectUrl": "https://webpay3gint.transbank.cl/webpayserver/initTransaction",
  "redirectToken": "01ab...",
  "bankDetails": null
}
```

**La respuesta se bifurca según la pasarela:**

| Pasarela | Qué viene | Qué hace el front |
|---|---|---|
| `Transfer` | `bankDetails` (JSON con datos bancarios), `redirectUrl: null` | Mostrar los datos y el monto; el comprador transfiere por fuera |
| `Webpay` | `redirectUrl` + `redirectToken` | **POST auto-submit** (abajo) |
| `MercadoPago` | `redirectUrl` (init point), `redirectToken: null` | `window.location = redirectUrl` |

| Error | Status | Código |
|---|---|---|
| Pasarela no configurada por la tienda | 409 | `payment.gateway_not_configured` |
| Sin saldo pendiente | 409 | `order.already_paid` |
| **Pedido cancelado** (venció su reserva, o lo canceló la tienda) | 409 | `order.cancelled` |

> `order.cancelled` acá es una protección de plata: sin ella el comprador se iba a la pasarela a
> pagar un pedido que ya no existe, y la plata quedaba cobrada esperando un reembolso a mano.
> Dejó de ser un caso raro cuando las reservas empezaron a vencer solas.

### Webpay: el redirect es un POST, no un GET

Transbank exige un **form POST** con el token en el campo `token_ws`:

```html
<form id="tbk" method="POST" action="{redirectUrl}">
  <input type="hidden" name="token_ws" value="{redirectToken}" />
</form>
<script>document.getElementById('tbk').submit();</script>
```

El comprador paga en Transbank y vuelve **al backend**, que confirma y lo redirige a tu página de
resultado:

```
{StorefrontResultUrl}?orderId=<guid>&status=approved|rejected|aborted|error
```

Hay que tener esa página (ej. `/pago/resultado`) y mostrar el mensaje según `status`. `aborted` =
el comprador canceló en Transbank (no es un error: ofrecé reintentar).

### Mercado Pago

Redirigís a `redirectUrl`. **La confirmación llega por webhook** (servidor a servidor), no por el
navegador: cuando el comprador vuelve, el pago **puede no estar aplicado todavía**. Mostrá
"estamos confirmando tu pago" y consultá el pedido cada pocos segundos.

> Mercado Pago está implementado pero **diferido**: sin probar end-to-end, sin `RefundAsync`
> verificado y con el formato del `x-signature` sin confirmar contra el panel de MP. Para el
> piloto, la tanda de verificación es solo Webpay.

### POST `/admin/payments/{paymentId}/confirm` — admin

Transferencia: la tienda confirma que recibió la plata.

```json
{ "externalReference": "TRF-2026-001" }
```

**200** → `{ paymentId, paymentState, orderId, orderPaymentStatus, orderBalance }`

> **`409 payment.already_resolved` NO es un fallo de pago** — significa "ya estaba confirmado". No
> muestres error alarmante: refrescá el pedido. Pasa normalmente con webhooks duplicados.

---

## 9 · Link de pago del invitado

El comprador sin cuenta accede a su pedido con el `accessToken` del checkout (o con un link que
la tienda le genera y le manda por WhatsApp).

### GET `/pay/{token}` — público, sin sesión

```json
{
  "orderId": "guid",
  "currency": "CLP",
  "total": 300000,
  "paid": 90000,
  "balance": 210000,
  "paymentStatus": "Deposited",
  "reservationExpiresAt": null,
  "lines": [ { "name": "Booster Box", "quantity": 2, "unitPrice": 150000 } ]
}
```

`reservationExpiresAt` viaja acá también: quien corre contra ese reloj es justamente quien tiene
este link. Es `null` cuando ya entró algún pago (el reloj se apagó) o cuando el pedido no está en
ventana.

Es a propósito **más flaca** que la vista del panel: sin `customerId`, sin estado de fulfillment,
sin referencias de pasarela.

### POST `/pay/{token}/initiate`

```json
{ "gateway": "Transfer" }
```

Cobra el **saldo** con la pasarela elegida. Misma respuesta que §8.

`404 order.payment_link_invalid` si el token no existe, expiró o fue reemplazado por uno nuevo.

---

## 10 · Área de cliente (`/account`)

Requiere sesión de cliente.

| Método | Ruta | Devuelve |
|---|---|---|
| GET | `/account/me` | `{ id, name }` |
| GET | `/account/orders` | Lista: `{ id, createdAt, fulfillmentStatus, paymentStatus, currency, total, paid, balance, lineCount }` |
| GET | `/account/orders/{orderId}` | Detalle — **mismo shape que `/pay/{token}`**, `reservationExpiresAt` incluido |

Pedir el pedido de otro cliente devuelve **404**, no 403 (no confirmamos que exista).

---

## 11 · Panel de tienda (`/admin`)

Requiere sesión de staff **de esa tienda** + header CSRF en las escrituras.

### Catálogo

| Método | Ruta | Notas |
|---|---|---|
| POST | `/admin/products` | Producto simple: `{ sku, name, description?, price, currency }`. Crea su variante default. **SKU único por tienda** |
| POST | `/admin/products/with-options` | Producto con ejes (ver abajo) |
| PUT/GET | `/admin/products/{id}/purchase-limit` | Límite de producto |
| PUT/GET | `/admin/variants/{id}/purchase-limit` | Límite de variante |

El GET del panel devuelve la política **completa** (`maxPerOrder`, `maxPerCustomer`,
`windowDays`) — es el único lugar donde se ve entera.

Producto con options — ejes ortogonales, variantes como combinaciones (admite matriz rala):

```json
{
  "name": "Blister Paldea Evolved",
  "options": [
    { "name": "Carta",  "values": ["Charizard", "Meganium"] },
    { "name": "Idioma", "values": ["Inglés", "Español"] }
  ],
  "variants": [
    { "sku": "BLI-CHAR-EN", "price": 12990, "currency": "CLP",
      "selections": { "Carta": "Charizard", "Idioma": "Inglés" } },
    { "sku": "BLI-MEGA-EN", "price": 11990, "currency": "CLP",
      "selections": { "Carta": "Meganium", "Idioma": "Inglés" } }
  ]
}
```

Errores: `catalog.duplicate_sku` (409), `catalog.duplicate_combination` (409),
`catalog.incomplete_selection` / `catalog.unknown_selection` / `catalog.empty_option` /
`catalog.duplicate_option` / `catalog.duplicate_option_value` / `catalog.no_variants` (400).

Límites (body): `{ maxPerOrder?, maxPerCustomer?, windowDays? }` — todos opcionales pero **al
menos un máximo**. `windowDays` es la ventana de `maxPerCustomer` (null = histórico) y se ignora
sin él.

### Stock

`POST /admin/variants/{id}/stock` → `{ quantity, reason? }` → `{ productVariantId, available,
reserved }`. `404 inventory.variant_not_found` si la variante no existe.

Semántica: `available` = vendible; `reserved` = retenido por pedidos pendientes de entrega. Al
**entregar** baja `reserved` y no vuelve; al **cancelar**, vuelve a `available`.

### Preventas: el ciclo completo

| Método | Ruta | Qué hace |
|---|---|---|
| PUT | `/admin/variants/{id}/preorder` | **Abre** el drop (upsert) |
| GET | `/variants/{id}/preorder` | Estado del drop |
| POST | `/admin/variants/{id}/preorder/close` | **Cierra** el drop |

Abrir:

```json
{ "capacity": 100, "releaseDate": "2026-09-01T00:00:00Z",
  "depositType": "Percentage", "depositValue": 30 }
```

`400 preorder.capacity_below_sold` si bajás el cupo por debajo de lo vendido.

**El ciclo de vida, en orden:**

1. **PUT preorder** — la variante pasa a venderse por cupo. El checkout prefiere una preventa
   activa sobre el stock **sin condición**, así que desde acá el stock físico no se toca.
2. Los compradores reservan cupo y pagan el **abono**.
3. **Llega la mercadería**: la tienda la carga con `/admin/variants/{id}/stock`. La vidriera
   **sigue** mostrando cupo, porque el drop sigue abierto.
4. **`POST /admin/orders/{id}/release`** por cada pedido pagado: convierte esa preventa a
   reserva de stock físico. Mueve **ese pedido**, no el drop.
5. **`POST /admin/variants/{id}/preorder/close`** — el drop terminó. La variante vuelve a
   venderse del stock, se cobra el total en vez del abono, y se reservan unidades reales.

> **El paso 5 es obligatorio y es de la tienda.** Sin él la variante sigue vendiéndose por cupo
> para siempre: los compradores pagan un abono por mercadería que ya está en el depósito, cada
> pedido queda esperando otro `release` a mano, y las unidades físicas quedan inalcanzables detrás
> del contador de cupo. Es un hecho **del drop** (llegó la mercadería, se acabó la ventana), por
> eso no lo dispara el primer `release`: un pedido cualquiera terminaría el drop de todos.

Cerrar responde `200` con el preorder en `status: "Closed"`. Errores: `409
preorder.already_closed` (ya estaba cerrado — no es idempotente a propósito), `404
preorder.not_found`.

**Cerrar no se deshace.** Un PUT sobre un drop cerrado responde `409 preorder.closed`, no lo
reabre: `soldCount` todavía cuenta el drop anterior, así que cancelar uno de sus pedidos viejos
le devolvería cupo a un drop del que esas unidades nunca fueron parte. Volver a correr un drop
sobre la misma variante es una decisión pendiente.

El cupo no vendido **no se convierte en nada**: la capacidad de un drop siempre fue una promesa
sobre unidades que no habían llegado. Lo que se vende después es el stock que el local cargó.

### Pedidos

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/admin/orders/{id}` | Detalle completo (líneas, montos, estados, `refundedAmount`, `reservationExpiresAt`) |
| POST | `/admin/orders/{id}/payments` | Registrar pago manual: `{ amount }` (atajo; el flujo oficial es §8) |
| POST | `/admin/orders/{id}/release` | Liberar preventa (ver aviso abajo) |
| POST | `/admin/orders/{id}/prepare` | Empezar preparación |
| POST | `/admin/orders/{id}/lines/{lineId}/fulfill` | Entregar: `{ quantity }` (parcial o total) |
| POST | `/admin/orders/{id}/cancel` | Cancelar y liberar reservas |
| POST | `/admin/orders/{id}/payment-link` | Generar link de pago: `{ validForDays? }` (default 30) |

> **`release` requiere stock cargado.** Antes de liberar un pedido de preventa, la tienda debe
> cargar el stock que llegó. El release **reserva ese stock físico** para el pedido. Si no llegó →
> `409 order.preorder_stock_missing`. El cupo del drop no se devuelve: queda consumido.

> **El payment link se muestra UNA sola vez.** No se puede volver a consultar (guardamos solo su
> hash). Generar uno nuevo invalida el anterior. Copiá el link al mostrarlo.

### Pagos y reembolsos

| Método | Ruta | Notas |
|---|---|---|
| GET | `/admin/orders/{id}/payments` | Cargos del pedido con `refundedAmount` de cada uno |
| POST | `/admin/payments/{id}/confirm` | Confirmar (transferencia) |
| POST | `/admin/payments/{id}/refund` | Reembolsar: `{ amount?, reason? }` — sin `amount` devuelve todo lo que queda |
| PUT | `/admin/payment-methods/{gateway}` | **Solo Owner**: `{ credentialsJson }` (string JSON) |

El reembolso apunta a **un cargo concreto**, no al pedido (un pedido con abono + saldo tiene dos
cargos). Un reembolso rechazado por la pasarela vuelve **200** con `state: "Failed"` y
`failureReason` — no es un error HTTP, es información.

Las credenciales **nunca** se devuelven: no hay GET de `/admin/payment-methods`.

### Usuarios de la tienda (solo Owner/Admin)

| Método | Ruta |
|---|---|
| GET | `/admin/staff` |
| POST | `/admin/staff` → `{ email, name, password, role }` |
| POST | `/admin/staff/{id}/deactivate` · `/activate` |

Desactivar **cierra las sesiones activas del usuario al instante**.

---

## 12 · Plataforma (`/platform`) — solo LeWiK

| Método | Ruta |
|---|---|
| GET | `/platform/stores` |
| POST | `/platform/stores` → `{ name, slug, customDomain?, ownerEmail, ownerName, ownerPassword }` |
| POST | `/platform/stores/{id}/suspend` · `/activate` |

`slug` debe ser una etiqueta DNS válida (minúsculas, dígitos, guiones). Suspender corta la tienda
de inmediato: todo devuelve `403 platform.store_suspended` salvo `/auth/staff`, `/auth/platform`
y `/health`.

---

## 13 · Estados del pedido

Dos ejes **independientes**.

### `paymentStatus`

| Valor | Significado |
|---|---|
| `Pending` | Nada pagado |
| `Deposited` | Pago parcial |
| `Paid` | Pagado completo |

Para "abono completo" comparar `paid >= depositDue` (cualquier monto parcial marca `Deposited`).

También viene `refundedAmount`: los reembolsos son un eje aparte y **no** hacen retroceder
`paymentStatus` (que registra lo cobrado). Mostrá "pagado" y "reembolsado" por separado.

`paymentStatus` y `fulfillmentStatus` son de verdad independientes: un pedido cancelado puede
seguir en `Deposited`. No infieras uno del otro.

### `fulfillmentStatus`

| Valor | Cómo se llega |
|---|---|
| `PendingPayment` | Inicial. Un pedido **solo-stock** se queda acá aunque haya pagos parciales |
| `AwaitingRelease` | **Solo preventas**: tras el primer pago, espera que llegue el stock |
| `Paid` | Listo para preparar (pago completo en solo-stock, o `release` en preventa) |
| `Preparing` | `POST /prepare` |
| `PartiallyDelivered` | `fulfill` con líneas incompletas |
| `Delivered` | Todas las líneas completas |
| `Cancelled` | `POST /cancel` o vencimiento de la reserva |

Tres reglas:

- Nada avanza a preparación/entrega con saldo pendiente (`order.balance_pending`).
- **`AwaitingRelease` es exclusivo de preventas.** Un pedido solo-stock nunca pasa por ahí ni
  necesita `release`.
- Después de cerrar un drop, los pedidos nuevos de esa variante son pedidos de stock: no pasan
  por `AwaitingRelease` ni necesitan `release`.

---

## 14 · Flujos completos

### A — Compra desde stock

1. `GET /storefront` → elegir variante (`availability.kind == "Stock"`).
2. `POST /orders` → guardar `accessToken`, mostrar contador con `reservationExpiresAt`.
3. `POST /orders/{id}/payments/initiate` → pagar según pasarela (§8).
4. (Transferencia) la tienda confirma → `Paid`.
5. Panel: `prepare` → `fulfill` (parcial o total) → `Delivered`.

### B — Preventa con abono, de punta a punta

1. Variante con `kind == "Preorder"` → `POST /orders`. El pedido nace con `depositDue < total`.
2. `initiate` con `type: "Deposit"` → confirmar → `Deposited` / `AwaitingRelease`.
3. **Llega el stock**: la tienda lo carga con `/admin/variants/{id}/stock`. *(La vidriera sigue
   mostrando cupo: el drop sigue abierto.)*
4. Cobrar el saldo: la tienda genera `payment-link` y lo manda, o `initiate` con
   `type: "Balance"`.
5. `POST /admin/orders/{id}/release` → reserva el stock para ese pedido → `Paid`.
6. `prepare` → `fulfill` → `Delivered`.
7. **`POST /admin/variants/{id}/preorder/close`** → el drop terminó y la variante vuelve a
   venderse del stock. Sin este paso sigue vendiéndose por cupo.

### C — Invitado que vuelve a pagar

1. Abre `/pagar/{accessToken}` → `GET /pay/{token}` muestra su pedido y saldo.
2. `POST /pay/{token}/initiate` → paga.

### D — Invitado que se registra

1. Compró antes con `juan@test.cl` (invitado).
2. `POST /auth/customer/register` con **ese mismo email** → cuenta creada sobre el registro
   existente.
3. `GET /account/orders` → **aparecen sus compras anteriores**.

Registrarse **no** esquiva los límites anti-scalping: es el mismo cliente.

### E — Página de drop con contador

1. Conectar SignalR y `WatchVariant(variantId)` → el valor actual viene en la misma llamada.
2. Actualizar `kind`, número y botón con cada `availabilityChanged`.
3. `UnwatchVariant` al salir.

*(`GET /storefront` sigue siendo la fuente del resto de la página: nombre, precio, opciones,
límite.)*

---

## 15 · Códigos de error

| Código (`title`) | Status |
|---|---|
| `request.invalid_body` · `request.csrf_header_missing` | 400 · 403 |
| `auth.invalid_credentials` · `auth.account_disabled` | 400 · 409 |
| `customer.already_registered` · `customer.not_found` | 409 · 404 |
| `platform.store_not_found` · `platform.staff_not_found` | 404 |
| `platform.slug_taken` · `platform.domain_taken` · `platform.staff_email_taken` | 409 |
| `platform.store_suspended` | 403 (middleware) · 409 (login de staff) |
| `catalog.product_not_found` · `catalog.no_purchase_limit` | 404 |
| `catalog.duplicate_sku` · `catalog.duplicate_combination` | 409 |
| `catalog.incomplete_selection` · `catalog.unknown_selection` · `catalog.empty_option` · `catalog.duplicate_option_value` · `catalog.duplicate_option` · `catalog.no_variants` | 400 |
| `inventory.not_found` · `inventory.variant_not_found` | 404 |
| `inventory.insufficient_stock` · `inventory.insufficient_reserved` | 409 |
| `preorder.not_found` | 404 |
| `preorder.capacity_exceeded` · `preorder.closed` · `preorder.already_closed` | 409 |
| `preorder.capacity_below_sold` | 400 |
| `order.not_found` · `order.variant_not_found` · `order.line_not_found` · `order.payment_link_invalid` | 404 |
| `order.no_stock` · `order.cancelled` · `order.already_paid` · `order.payment_exceeds_balance` · `order.balance_pending` · `order.cannot_cancel_delivered` · `order.fulfill_exceeds_pending` · `order.invalid_transition` · `order.limit_per_order` · `order.limit_per_customer` · `order.preorder_stock_missing` · `order.nothing_to_pay` · `order.refund_exceeds_paid` | 409 |
| `order.empty` · `order.mixed_currency` | 400 |
| `payment.not_found` · `payment.token_not_found` | 404 |
| `payment.already_resolved` · `payment.gateway_not_configured` · `payment.invalid_credentials` · `payment.unsupported_currency` · `payment.gateway_failure` | 409 |
| `refund.already_resolved` · `refund.payment_not_refundable` · `refund.exceeds_payment` · `refund.not_supported` | 409 |

Dos códigos existen pero **el front nunca los ve**: `order.reservation_not_expired` (lo usa el
job que vence reservas para abortar si el pedido se pagó mientras tanto) y
`payment.invalid_webhook_signature` (superficie servidor-a-servidor de Mercado Pago).

---

## 16 · Limitaciones y notas

1. **El registro de clientes es bloqueante para producción sin verificación de email.** Ver §4.
2. **Mercado Pago está diferido**: sin probar end-to-end, `RefundAsync` sin verificar contra
   pasarela real, y el formato del `x-signature` sin confirmar. Para el piloto, solo Webpay.
3. **Sin recuperación de contraseña ni emails.** No hay envío de correos: ni reset, ni avisos de
   "llegó tu drop". El pago del saldo se avisa manualmente con el payment link.
4. **Sin paginación** en `/storefront`, catálogo, pedidos ni staff, y sin caché. Filtrar
   client-side; los volúmenes del piloto lo permiten. `/storefront` es el endpoint que va a comer
   el pico de un drop — si aparece latencia, es lo primero a medir.
5. **Sin reportería.** No hay endpoints de ventas por período ni top productos (Fase 6).
6. **El payment link no se puede volver a ver.** Guardamos solo su hash. Copiarlo al generarlo.
7. **Un drop cerrado no se reabre**, y no hay forma de correr un segundo drop sobre la misma
   variante (§11).
8. **`releaseDate` es informativa.** Nada la mira salvo para mostrarla: pasada la fecha el drop
   sigue vendiendo por cupo hasta que alguien lo cierre.
9. **El snapshot del hub trae solo `{ variantId, kind, available, isSellable }`** — sin
   `releaseDate` ni condiciones de abono. Para eso está `/storefront`.
10. **`detail` viene en inglés** — localizar por `title`.
11. **SignalR es mejora, no fuente de verdad.** Si no conecta, la página debe funcionar igual.
12. **Ocultar por rol no es proteger**: el backend valida igual, pero la UI debería reflejar los
    permisos para no ofrecer acciones que van a fallar.
13. **CORS es una lista fija de orígenes.** Con dominios propios por tienda va a necesitar
    validación dinámica contra la tabla de tiendas — pendiente, y bloqueante para ese escenario.

---

## 17 · Datos para los E2E

```bash
./scripts/seed-e2e.sh
```

Deja la base con un dataset **determinista** y escribe el manifiesto en
`scripts/e2e-seed.json` con todos los ids. Correrlo de nuevo borra lo anterior y reconstruye lo
mismo, así que un test puede afirmar "quedan 98 cupos" sin cuidar el orden ni limpiar después.
Los slugs, SKUs, emails y contraseñas son fijos; **los GUIDs no**, por eso el manifiesto: la
suite lo importa en vez de tener ids escritos a mano.

Sembrar requiere sesión de plataforma y de staff, pero **lo sembrado se consume sin autenticarse**
— la vidriera, el checkout, el pago y el contador en vivo son todos anónimos.

### Qué queda sembrado

| Fixture | Para probar |
|---|---|
| `variants.inStock` (`E2E-BOX`) | compra feliz desde stock |
| `variants.outOfStock` (`E2E-AGOTADO`) | `isSellable: false`. Está **vendido**, no inexistente: tiene fila de inventario, así que un checkout da `inventory.insufficient_stock` y no `order.no_stock` |
| `variants.limited` (`E2E-LIMITADO`) | `maxPerOrder: 2` visible + tope por cliente **oculto** de 3/30d → el 409 que el selector no puede anticipar |
| `variants.drop` (`E2E-DROP`) | página de drop, abono 30%, contador en vivo |
| `variants.dropAlmostGone` | queda **1** cupo: ver el contador llegar a cero y el `preorder.capacity_exceeded` del siguiente |
| `variants.dropClosed` | variante que **fue** drop, se liberó y se cerró: hoy vende por stock |
| `productWithOptions` | 2 ejes × 4 combinaciones, una sin stock: selector con opción no vendible |
| `variants.otherStore` | pertenece a `e2e-otra`: usar ese id contra `e2e` debe dar 404 |
| `stores.suspended` | todo 403 `platform.store_suspended` salvo `/auth/staff` y `/health` |
| `orders.*` | un pedido por estado: `pendingPayment` (con token y reloj corriendo), `awaitingRelease`, `paid`, `delivered`, `cancelled`, `withPaymentLink` |
| `orders.customerHistory` | compra hecha **como invitado** que quedó dentro de la cuenta al registrarse |
| `users.customer` | cliente con contraseña e historial · `users.owner` y `users.staff` para probar el recorte por rol |

La pasarela **Transfer ya está configurada**, así que `initiate` responde 200 sin que nadie toque
el panel.

> **El pedido `pendingPayment` se cancela solo a los 30 minutos** (`Reservations:TtlMinutes`). Si
> la suite lo necesita vivo, correr el seed antes de la tanda. Es la misma razón por la que el
> seed es barato de repetir.

> Si borrás tiendas a mano en la base, **acordate de limpiar la caché del resolver**:
> `docker exec lewik_store_redis redis-cli --scan --pattern 'tenant:host:*' | xargs redis-cli DEL`.
> Sin eso el host sigue resolviendo a un tenant que ya no existe y todo contesta
> `platform.store_not_found`, que es un síntoma bastante desconcertante. El seed ya lo hace.

### Correrlo en CI — todavía no se puede tal cual

El reset borra por `docker exec lewik_store_db psql`, porque **no hay endpoint que borre tiendas
y no debería haberlo**. En un runner donde la API es un servicio remoto ese `docker exec` no
existe, así que el script funciona en una máquina de desarrollo y no en un pipeline.

Tres salidas, sin decidir todavía:

| Opción | Qué implica |
|---|---|
| **Base efímera por corrida** | Levantar Postgres+Redis desde cero en el job y usar `--no-reset`. No hay nada que borrar. Es lo más limpio y probablemente lo que quieran |
| **Endpoint de reset gateado por entorno** | Un `POST /platform/test-reset` que solo exista fuera de producción. Rápido, pero agrega superficie destructiva al backend y hay que gatearla con el mismo cuidado que `Tenancy:AllowHeaderOverride` |
| **Correr el seed desde el runner con acceso a la base** | Sirve si el pipeline levanta los contenedores igual. Es la opción de hoy, sin cambios |

`--no-reset` ya existe y siembra sin borrar (falla si los slugs ya están tomados), así que la
primera opción funciona hoy sin tocar nada del backend. Cuando definan el pipeline lo cerramos.

### Si tu tooling de E2E invoca `curl` desde bash

En Git Bash sobre Windows, **`curl -d` corrompe los argumentos con caracteres no ASCII**: mandar
`"Colección"` en el nombre de un producto devuelve `400 request.invalid_body`, y los mismos bytes
por `--data-binary @-` devuelven 200. Se transcodifica el argumento antes de que curl lo vea; no
es el backend. Los cuerpos van por stdin:

```bash
printf '%s' "$json" | curl ... -H "Content-Type: application/json" --data-binary @-
```

No aplica si la suite usa `fetch` desde Node o un runner tipo Playwright/Cypress, que mandan los
bytes directo. Sí aplica a cualquier prueba manual con curl, y un catálogo chileno tiene acentos
por definición.

## 18 · Otras herramientas

- **Bruno** (`bruno/`) — 44 requests documentadas. Orden de arranque: *Auth > Platform Login* →
  *Platform > Create Store* → *Auth > Staff Login*, y recién ahí responde el resto.
- **`./scripts/smoke-test.sh`** — 452 aserciones end-to-end en ~50 segundos contra
  `http://localhost:5223`. Si algo del front se comporta raro, corré esto primero: si da verde,
  el problema está del lado del cliente.
- **`http://<slug>.localhost:5223/signalr-test.html`** — página del contador en vivo, servida por
  la API en Development.
