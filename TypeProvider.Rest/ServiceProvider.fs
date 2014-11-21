namespace TypeProvider.Rest

open System
open System.Reflection
open System.IO
open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices
open ProviderImplementation
open System.Text.RegularExpressions
open FSharp.Data
open System.Net.Http.Headers
open System.Net

open TypeProvider.Types

open TypeProvider.Rest.Helpers

type Service(address) =
    member __.Address = address

[<TypeProvider>]
type public RestfulProvider(cfg:TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()

    let determineType text =
        match text with
        | "string" -> Some(typeof<string>)
        | "IEnumerable<string>" -> Some(typeof<seq<string>>)
        | _ -> Some(typeof<obj>)

    let parseArray text =
        JsonValue.Parse(text).AsArray() |> Array.map (fun it -> it.AsString())

    let buildConstructor service typeText =       
        match typeText with
        | "string" -> ProvidedConstructor([], InvokeCode = fun [] -> <@@ doGet service id @@>),
                      ProvidedConstructor([ProvidedParameter("uri", typeof<string>)], InvokeCode = fun[uri] -> <@@ doGet %%uri id @@>)
        | "IEnumerable<string>" -> ProvidedConstructor([], InvokeCode = fun [] -> <@@ doGet service parseArray @@>),
                                   ProvidedConstructor([ProvidedParameter("uri", typeof<string>)], InvokeCode = fun[uri] -> <@@ doGet %%uri parseArray @@>)
        | _ -> ProvidedConstructor([]), ProvidedConstructor([ProvidedParameter("uri", typeof<string>)], InvokeCode = fun[uri] -> <@@ ignore @@>)

    let getDocs (root:String) (fragment:String) =
        async {
            let client = new System.Net.Http.HttpClient()
            client.DefaultRequestHeaders.Accept.Clear()
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("documentation/json"))

            let path = new Uri((root + fragment).Replace("//", "/").Replace(":/", "://"), UriKind.Absolute)
            
            let! response = client.GetAsync(path)
                            |> Async.AwaitTask

            if not (response.StatusCode = HttpStatusCode.OK) then 
                failwith "Invalid response"

            let! text = response.Content.ReadAsStringAsync()
                        |> Async.AwaitTask

            return new Descriptor(text)
        }

    let getName (root : string) =

        let (|NameAndType|Name|) (text : string) =
            if text.StartsWith "%" then
                let name :: typ :: _ = text.Split([|'%'; ':'|], StringSplitOptions.RemoveEmptyEntries)
                                       |> Array.toList
                NameAndType(name, typ)
            else
                Name(text)

        let nameSection = root.Substring(root.LastIndexOf("/") + 1)

        match nameSection with
        | NameAndType(name, t) -> name, Some(t)
        | Name(name) -> name, None

    let (|ParameterisedGet|StraightGet|NoGet|) (indexer, (verbs:Verb[])) =
        
        let get = verbs 
                  |> Array.tryFind (fun v -> v.Verb.Equals("get", StringComparison.OrdinalIgnoreCase))
                  
        if get.IsSome then
            match indexer with
            | Some(indexerType) -> ParameterisedGet(indexerType, get.Value)
            | None -> StraightGet(get.Value)
        else
            NoGet


    // Get the assembly and namespace used to house the provided types
    let asm = System.Reflection.Assembly.GetExecutingAssembly()
    let ns = "TypeProvider.Service"

    let AddVerb (parent:ProvidedTypeDefinition) verb =
        ()

    let rec BuildType serviceRoot methodFragment =
        async {
            let! docs = getDocs serviceRoot methodFragment

            let Name, indexerType = getName docs.Path

            let otherVerbs = docs.Verbs |> Array.filter (fun v -> not <| v.Verb.Equals("get", StringComparison.OrdinalIgnoreCase))

            let newType = 
                // If this resource has a straight get, then make it of the type that the get returns...
                match indexerType, docs.Verbs with
                | ParameterisedGet(index, g) -> 
                                    let containedType = determineType(g.Response)
                                    
                                    match (containedType) with
                                    | Some(contained) -> 
                                                         let genericType = typedefof<LazyIndexer<_>>
                                                         let realType = genericType.MakeGenericType(contained)

                                                         let t = ProvidedTypeDefinition(Name, Some(realType))

                                                         let defaultCons, parameterCons = buildConstructor serviceRoot g.Response

                                                         t.AddMember defaultCons
                                                         t.AddMember parameterCons

                                                         t                                                     
                                    | _ -> failwith "Unable to determine type for parameterised Get."
                | StraightGet(g) -> let t = ProvidedTypeDefinition(Name, determineType(g.Response))
                             
                                    let defaultCons, parameterCons = buildConstructor serviceRoot g.Response

                                    t.AddMember defaultCons
                                    t.AddMember parameterCons

                                    t
                | NoGet -> let t = ProvidedTypeDefinition(Name, Some(typeof<Service>))
                           // add a parameterless constructor which loads the service that was used to define the type
                           t.AddMember(ProvidedConstructor([], InvokeCode = fun [] -> <@@ Service(serviceRoot) @@>))

                           // add a constructor taking the filename to load
                           t.AddMember(ProvidedConstructor([ProvidedParameter("service", typeof<string>)], InvokeCode = fun [service] -> <@@ Service(%%service) @@>)) 
                           t
            
            for verb in otherVerbs do
                AddVerb newType verb
            
            for child in docs.Children do
                let uri = new Uri(serviceRoot)
                let strippedRoot = uri.ToString().Replace(uri.AbsolutePath, String.Empty)
                let newRoot = strippedRoot + docs.Path
                let! childType = BuildType newRoot child

                newType.AddMember childType

                // let childValue = new Service(newRoot)

//                let textInfo = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo
//
//                let newName = child.Replace("/", "")
//                             |> textInfo.ToTitleCase
//
//                let publicProp = ProvidedLiteralField(
//                    newName,
//                    childType,
//                    childValue)

//                newType.AddMember publicProp

            return newType
        }
    and AddMethods serviceRoot methodFragment (parent:ProvidedTypeDefinition) =
        async {
            ()
        }

    // Create the main provided type
    let restTy = ProvidedTypeDefinition(asm, ns, "Restful", Some(typeof<obj>))

    // Parameterize the type by the Url to use as a template
    let address = ProvidedStaticParameter("address", typeof<string>)
    do restTy.DefineStaticParameters([address], fun tyName [| :? string as address |] ->

        let serv = ProvidedTypeDefinition(asm, ns, tyName, Some(typeof<Service>))

        // add a parameterless constructor which loads the service that was used to define the type
        serv.AddMember(ProvidedConstructor([], InvokeCode = fun [] -> <@@ Service(address) @@>))

        // add a constructor taking the filename to load
        serv.AddMember(ProvidedConstructor([ProvidedParameter("service", typeof<string>)], InvokeCode = fun [service] -> <@@ Service(%%service) @@>)) 

        let ty = BuildType address "/" |> Async.RunSynchronously
        
        serv.AddMember ty

        serv
        )
    
    do this.AddNamespace(ns, [restTy])

[<TypeProviderAssembly>]
do()