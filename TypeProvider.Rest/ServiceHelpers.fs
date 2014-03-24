module TypeProvider.Rest.Helpers

    open System
    open System.Net

    let doGet (uri:string) parser =
        async {
            let client = new System.Net.Http.HttpClient()
            client.DefaultRequestHeaders.Accept.Clear()

            let! response = client.GetAsync(uri)
                            |> Async.AwaitTask

            if not (response.StatusCode = HttpStatusCode.OK) then 
                failwith "Invalid response"

            let! text = response.Content.ReadAsStringAsync()
                        |> Async.AwaitTask

            return parser text
        }
