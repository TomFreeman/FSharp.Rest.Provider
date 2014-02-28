namespace TypeProvider.Rest

open System
open System.Reflection
open System.IO
open Samples.FSharp.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices
open System.Text.RegularExpressions
open FSharp.Data
open System.Net.Http.Headers
open System.Net

type Service(address) =
    member __.Address = address

type descriptor = JsonProvider<"ServiceDescriptor.json">

[<TypeProvider>]
type public RestfulProvider(cfg:TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()

    let determineType text =
        match text with
        | "string" -> Some(typeof<string>)
        | _ -> Some(typeof<obj>)

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

            return descriptor.Parse(text)
        }

    // Get the assembly and namespace used to house the provided types
    let asm = System.Reflection.Assembly.GetExecutingAssembly()
    let ns = "TypeProvider.Service"

    let AddVerb (parent:ProvidedTypeDefinition) verb =
        ()

    let rec BuildType serviceRoot methodFragment =
        async {
            let! docs = getDocs serviceRoot methodFragment

            let Name = docs.Path.Substring(docs.Path.LastIndexOf("/") + 1)

            // If this resource has a straight get, then make it of the type that the get returns...
            let get = docs.Verbs |> Array.tryFind (fun v -> v.Verb.Equals("get", StringComparison.OrdinalIgnoreCase))
            let otherVerbs = docs.Verbs |> Array.filter (fun v -> not <| v.Verb.Equals("get", StringComparison.OrdinalIgnoreCase))

            let newType = 
                match get with
                | Some(g) -> let t = ProvidedTypeDefinition(Name, determineType(g.Response))
                             t
                | None -> ProvidedTypeDefinition(Name, Some(typeof<Service>))

            // add a parameterless constructor which loads the service that was used to define the type
            newType.AddMember(ProvidedConstructor([], InvokeCode = fun [] -> <@@ Service(serviceRoot) @@>))

            // add a constructor taking the filename to load
            newType.AddMember(ProvidedConstructor([ProvidedParameter("service", typeof<string>)], InvokeCode = fun [service] -> <@@ Service(%%service) @@>)) 

            for verb in otherVerbs do
                AddVerb newType verb
            
            for child in docs.Children do
                let uri = new Uri(serviceRoot)
                let strippedRoot = uri.ToString().Replace(uri.AbsolutePath, String.Empty)
                let newRoot = strippedRoot + docs.Path
                let! (childType:ProvidedTypeDefinition) = BuildType newRoot child

                newType.AddMember childType

                let childValue = new Service(newRoot)

                let textInfo = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo

                let newName = child.Replace("/", "")
                             |> textInfo.ToTitleCase

                let publicProp = ProvidedLiteralField(
                    newName,
                    childType,
                    childValue)

                newType.AddMember publicProp

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