﻿namespace FSharpIL.Metadata

open System.Collections.Immutable

[<Interface>]
type IMetadataTable<'Row> =
    abstract Count: int32
    abstract Item: RawIndex<'Row> -> 'Row with get

[<Sealed>]
type MetadataTable<'Row> internal (items: ImmutableArray<'Row>) =
    member _.Count = items.Length
    member _.Rows = items
    member _.Item with get (index: RawIndex<'Row>) = items.[index.Value - 1]

    interface IMetadataTable<'Row> with
        member this.Count = this.Count
        member this.Item with get index = this.[index]

namespace global

open FSharpIL.Metadata
open FSharpIL.Writing

[<AutoOpen>]
module MetadataTableExtensions =
    type IMetadataTable<'Row> with
        /// Gets a value indicating whether or not a simple index into this table takes up four or two bytes.
        member this.HasLargeIndices = this.Count >= 65536

        member internal this.WriteSimpleIndex(i: RawIndex<'Row>, writer: ChunkWriter) =
            if this.HasLargeIndices
            then writer.WriteU4 i
            else writer.WriteU2 i
