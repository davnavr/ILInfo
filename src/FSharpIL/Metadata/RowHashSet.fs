﻿namespace FSharpIL.Metadata

open System
open System.Collections.Generic
open System.Collections.Immutable

/// Contains the rows of a metadata table, and prevents the addition of duplicate rows.
[<System.Runtime.CompilerServices.IsReadOnly>]
type RowHashSet<'T when 'T : equality> = struct
    val private items: ImmutableDictionary<'T, int32>.Builder
    val private owner: IndexOwner

    internal new (owner) = { items = ImmutableDictionary.CreateBuilder<_, _>(); owner = owner }

    member this.Count = this.items.Count

    member this.TryAdd item =
        if this.items.TryAdd(item, this.Count)
        then this.CreateIndex item |> ValueSome
        else ValueNone

    member this.GetEnumerator() = ArraySegment<_>(this.ToArray()).GetEnumerator()

    member internal this.ToArray(): 'T[] =
        let items' = Array.zeroCreate this.Count
        for KeyValue(key, i) in this.items do items'.[i] <- key
        items'

    member internal this.ToImmutable() =
        let items' = Array.zeroCreate this.Count
        let lookup = Dictionary<SimpleIndex<'T>, int32> this.Count
        for KeyValue(key, i) in this.items do
            lookup.[this.CreateIndex key] <- i + 1
            items'.[i] <- key
        { TableOwner = this.owner
          TableItems = items'.ToImmutableArray()
          TableLookup = lookup }

    member private this.CreateIndex item = SimpleIndex<'T>(this.owner, item)

    interface IReadOnlyCollection<'T> with
        member this.Count = this.Count
        member this.GetEnumerator() = this.GetEnumerator() :> IEnumerator<_>
        member this.GetEnumerator() = this.GetEnumerator() :> System.Collections.IEnumerator
end