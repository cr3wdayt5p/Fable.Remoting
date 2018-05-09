namespace Fable.Remoting.DotnetClient

open Fable.Remoting.Json
open Newtonsoft.Json
open System.Net.Http

[<RequireQualifiedAccess>]
module Proxy =

    open Patterns
    let private converter = FableJsonConverter()

    /// Parses a JSON iput string to a .NET type using Fable JSON converter
    let parseAs<'t> (json: string) =
        JsonConvert.DeserializeObject<'t>(json, converter)

    /// Sends a POST request to the specified url with the arguments of serialized to an input list
    let proxyPost<'t> (functionArguments: obj list) url client auth =
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        async {
            let! responseText = Http.makePostRequest client url serializedInputArgs auth
            return parseAs<'t> responseText
        }

    /// Sends a POST request to the specified url safely with the arguments of serialized to an input list, if an exception is thrown, is it catched
    let safeProxyPost<'t> (functionArguments: obj list) url client auth =
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        async {
            let! catchedResponse = Async.Catch (Http.makePostRequest client url serializedInputArgs auth)
            match catchedResponse with
            | Choice1Of2 responseText -> return Ok (parseAs<'t> responseText)
            | Choice2Of2 thrownException -> return Error thrownException
        }

    type Proxy<'t>(builder, client: Option<HttpClient>) =
        let typeName =
            let name = typeof<'t>.Name
            match typeof<'t>.GenericTypeArguments with
            | [|  |] -> name
            | manyArgs -> name.[0 .. name.Length - 3]
        let mutable authHeader = Http.Authorisation.NoToken
        let client = defaultArg client (new HttpClient())
        /// Uses the specified string as the authorization header for the requests that the proxy makes to the server
        member this.authorisationHeader (header: string) =
            authHeader <- Http.Authorisation.Token header

        /// Call the proxy function by wrapping it inside a quotation expr:
        /// ```
        /// async {
        ///     let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///     let! result = proxy.call <@ server -> server.getLength "input" @>
        ///  }
        /// ```
        member this.call<'u> (expr: Quotations.Expr<'t -> Async<'u>>) =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                proxyPost<'u> args route client authHeader
            | otherwise -> failwithf "Failed to process the following quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

        /// Call the proxy function safely by wrapping it inside a quotation expr and catching any thrown exception by the web request
        /// ```
        ///    async {
        ///       let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///       let! result = proxy.callSafely <@ server -> server.getLength "input" @>
        ///       match result with
        ///       | Ok result -> (* do stuff with result *)
        ///       | Error ex -> (* panic! *)
        ///    }
        /// ```
        member this.callSafely<'u> (expr: Quotations.Expr<'t -> Async<'u>>) : Async<Result<'u, exn>> =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                safeProxyPost<'u> args route client authHeader
            | otherwise -> failwithf "Failed to process quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

    /// Creates a proxy for a type with a route builder
    let create<'t> builder = Proxy<'t>(builder)
    /// Creates a proxy with for a type using a route builder and a custom HttpClient that you provide
    let custom<'t> builder client = Proxy<'t>(builder, client)
