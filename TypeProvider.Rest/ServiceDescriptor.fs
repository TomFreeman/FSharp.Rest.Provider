namespace TypeProvider.Types

open System
open FSharp.Data
open FSharp.Data.Runtime

type Verb(json:JsonValue) =
    member this.Verb = json.Item("verb").AsString()
    member this.Response = json.Item("response").AsString()
    member this.Body = json.Item("body").AsString()

type Descriptor(json:String) =

    let parsed = JsonValue.Parse(json)

    member this.Verbs =
        parsed.TryGetProperty("verbs")
        |> fun opt ->
            match opt with
            | None -> Array.empty
            | Some(o) -> o.AsArray() |> Array.map (fun item -> new Verb(item))

    member this.Path = parsed.Item("path").AsString()
    member this.Children = parsed.TryGetProperty("children")
                           |> fun opt ->
                                match opt with 
                                | None -> Array.empty
                                | Some(o) -> o.AsArray() |> Array.map (fun item -> item.AsString())