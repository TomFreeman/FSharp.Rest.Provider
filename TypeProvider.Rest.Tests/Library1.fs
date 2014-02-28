module TypeProvider.Rest.Tests

open FsUnit
open FsUnit.Xunit
open Xunit

type check = TypeProvider.Service.Restful<address="http://localhost:55967/api">

[<Fact>]
let ``The Type provider builds something!``() = 
    let test = new check()
    test |> should not' (be Null)

[<Fact>]
let ``The Type provider built something with the right properties!``() =
    let test = new check.Values()
    test |> should not' (be Null)