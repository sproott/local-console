namespace MF.Rohlik

open System
open System.IO
open System.Net
open System.Web
open FSharp.Data
open FSharp.Data.HttpRequestHeaders
open MF.Utils
open Feather.ErrorHandling

type Credentials = {
    Username: string
    Password: string
}

type UserInfo = {
    UserId: int option
    AddressId: int option
}

type LoginResult = {
    Status: int
    UserInfo: UserInfo
    Message: string option
}

type OrderProduct = {
    Id: string
    Name: string
    Quantity: int
    Price: decimal
}

type Order = {
    Id: string
    Date: DateTimeOffset
    TotalPrice: decimal
    Products: OrderProduct list
}

type ProductSummary = {
    Name: string
    TotalQuantity: int
    OrderCount: int
    LastOrderDate: DateTimeOffset
    AveragePrice: decimal
}

[<RequireQualifiedAccess>]
module Api =
    let private baseUrl = "https://www.rohlik.cz"
    let mutable private infoMode = false
    let mutable private debugMode = false

        // Helper function to decode HTML entities and fix UTF-8 encoding issues
    let decodeHtml (text: string) =
        let htmlDecoded = System.Web.HttpUtility.HtmlDecode(text)
        try
            // Fix UTF-8 bytes interpreted as Latin-1
            let latin1Bytes = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(htmlDecoded)
            System.Text.Encoding.UTF8.GetString(latin1Bytes)
        with
        | _ -> htmlDecoded // If conversion fails, return HTML decoded version

    let enableInfo () =
        infoMode <- true

    let enableDebug () =
        enableInfo()
        debugMode <- true

    let private teeDebug f a =
        if debugMode then f a
        a

    // JSON Type Providers for API responses
    type private LoginResponseSchema = JsonProvider<"""
        {
            "status": 200,
            "data": {
                "user": {
                    "id": 12345
                },
                "address": {
                    "id": 67890
                }
            },
            "messages": [
                {
                    "content": "Success"
                }
            ]
        }
    """>

    type private OrdersResponseSchema = JsonProvider<"""
        {
            "id": 1107296263,
            "itemsCount": 10,
            "priceComposition": {
                "total": {
                    "amount": 989.49,
                    "currency": "CZK"
                }
            },
            "orderTime": "2025-08-11T23:52:21.000+0200",
            "deliverySlot": null,
            "pblLink": null
        }
    """>

    type private OrderDetailSchema = JsonProvider<"schema/orderDetail.json", SampleIsList=true>

    let private api httpMethod path (cookies: CookieContainer option) (postData: (string * string) list option) =
        asyncResult {
            let url = sprintf "%s%s" baseUrl path

            let headers = [
                Accept "application/json"
                ContentType "application/json"
                UserAgent "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)"
            ]

            let cookieContainer = cookies |> Option.defaultValue (CookieContainer())

            let! response =
                match httpMethod, postData with
                | "GET", None ->
                    Http.AsyncRequestString(
                        url,
                        httpMethod = "GET",
                        headers = headers,
                        cookieContainer = cookieContainer
                    ) |> AsyncResult.ofAsyncCatch id

                | "POST", Some data ->
                    let jsonBody =
                        data
                        |> List.map (fun (k, v) -> sprintf "\"%s\":\"%s\"" k v)
                        |> String.concat ","
                        |> sprintf "{%s}"

                    Http.AsyncRequestString(
                        url,
                        httpMethod = "POST",
                        headers = headers,
                        body = TextRequest jsonBody,
                        cookieContainer = cookieContainer
                    ) |> AsyncResult.ofAsyncCatch id

                | "POST", None ->
                    Http.AsyncRequestString(
                        url,
                        httpMethod = "POST",
                        headers = headers,
                        cookieContainer = cookieContainer
                    ) |> AsyncResult.ofAsyncCatch id

                | _ -> AsyncResult.ofError (failwith "Unsupported HTTP method")

            return response
        }
        |> AsyncResult.teeError (fun e ->
            if debugMode then eprintfn "[Api] %s error: %A" path e
        )

    let login (credentials: Credentials) = asyncResult {
        let cookies = CookieContainer()
        let loginData = [
            "email", credentials.Username
            "password", credentials.Password
            "name", ""
        ]

        let! responseText = api "POST" "/services/frontend-service/login" (Some cookies) (Some loginData)

        let response = LoginResponseSchema.Parse(responseText)

        let result =
            if response.Status = 200 then
                {
                    Status = response.Status
                    UserInfo = {
                        UserId = response.Data.User |> Option.ofObj |> Option.map (fun u -> u.Id)
                        AddressId = response.Data.Address |> Option.ofObj |> Option.map (fun a -> a.Id)
                    }
                    Message = None
                }
            elif response.Status = 401 then
                {
                    Status = response.Status
                    UserInfo = { UserId = None; AddressId = None }
                    Message = response.Messages |> Array.tryHead |> Option.map (fun m -> m.Content)
                }
            else
                {
                    Status = response.Status
                    UserInfo = { UserId = None; AddressId = None }
                    Message = response.Messages |> Array.tryHead |> Option.map (fun m -> m.Content)
                }

        return result, cookies
    }

    let logout (cookies: CookieContainer) = asyncResult {
        let! _ = api "POST" "/services/frontend-service/logout" (Some cookies) None
        return ()
    }

    let getOrderDetail (cookies: CookieContainer) orderId: AsyncResult<OrderProduct list, _> = asyncResult {
        if infoMode then printfn " - fetch order details for %s" orderId
        let path = sprintf "/api/v3/orders/%s" orderId

        let! responseText = api "GET" path (Some cookies) None
        let response = OrderDetailSchema.Parse(responseText)

        return
            response.Items
            |> Array.map (fun item ->
                {
                    Id = string item.Id
                    Name = item.Name |> decodeHtml
                    Quantity = item.Amount
                    Price = item.PriceComposition.Unit.Amount
                }
            )
            |> Array.toList
    }

    let getDeliveredOrders (cookies: CookieContainer) (limit: int) = asyncResult {
        let path = sprintf "/api/v3/orders/delivered?offset=0&limit=%d" limit

        let! responseText = api "GET" path (Some cookies) None
        let response = OrdersResponseSchema.ParseList(responseText)
        if debugMode then printfn "Order response:\n%A" response

        let orders =
            response
            |> Array.map (fun order ->
                {
                    Id = string order.Id
                    Date = order.OrderTime
                    TotalPrice = order.PriceComposition.Total.Amount
                    Products = []
                }
            )
            |> Array.toList
            |> teeDebug (List.length >> printfn "[Api] Loaded %d delivered orders")

        return!
            orders
            |> List.map (fun order -> asyncResult {
                let! products =
                    match order.Products with
                    | [] -> getOrderDetail cookies order.Id
                    | products -> AsyncResult.ofSuccess products

                return { order with Products = products }
            })
            |> AsyncResult.ofMaxParallelAsyncResults 5 id
            |> AsyncResult.mapError List.head
    }

    let getAllProductsFromOrders (orders: Order list) =
        orders
        |> List.collect (fun order ->
            order.Products
            |> List.map (fun product ->
                order.Date, product
            )
        )
        |> List.groupBy (fun (_, product) -> product.Name.ToLowerInvariant())
        |> List.map (fun (productName, occurrences) ->
            let totalQuantity = occurrences |> List.sumBy (fun (_, product) -> product.Quantity)
            let lastOrder = occurrences |> List.map fst |> List.max
            let sample = occurrences |> List.head |> snd

            {
                Name = sample.Name
                TotalQuantity = totalQuantity
                OrderCount = occurrences.Length
                LastOrderDate = lastOrder
                AveragePrice = occurrences |> List.averageBy (fun (_, product) -> float product.Price) |> decimal
            }
        )
        |> List.sortByDescending (fun p -> p.OrderCount)

    // Helper function to create session with login
    let createSession (credentials: Credentials) = asyncResult {
        let! loginResult, cookies = login credentials

        if loginResult.Status = 200 then
            return cookies, loginResult.UserInfo
        else
            return! AsyncResult.ofError (failwithf "Login failed: %s" (loginResult.Message |> Option.defaultValue "Unknown error"))
    }

    // Main function to get product summary from order history
    let getOrderHistoryProductSummary (credentials: Credentials) (orderLimit: int) = asyncResult {
        let! cookies, userInfo = createSession credentials
        let! orders = getDeliveredOrders cookies orderLimit
        let! _ = logout cookies

        let productSummary = getAllProductsFromOrders orders

        return productSummary
    }
