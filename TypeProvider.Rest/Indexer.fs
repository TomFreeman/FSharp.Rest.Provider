namespace TypeProvider.Rest

    type LazyIndexer<'a>() =
        member this.Item
            with get(index) = Unchecked.defaultof<'a>