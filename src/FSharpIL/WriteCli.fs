﻿[<RequireQualifiedAccess>]
module internal FSharpIL.WriteCli

open FSharp.Core.Operators.Checked

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Runtime.CompilerServices

open FSharpIL.Metadata
open FSharpIL.Metadata.Heaps
open FSharpIL.Writing

[<IsReadOnly; IsByRefLike; Struct>]
type CodedIndex<'T> internal (count: int32, n: int32, indexer: 'T -> uint32 * uint32) =
    member _.LargeIndices = count > (65535 >>> n)

    member _.IndexOf (item: 'T) =
        let index, tag = indexer item
        (index <<< n) ||| tag
    
    member this.WriteIndex(item, writer: ChunkWriter) =
        let index = this.IndexOf item
        if this.LargeIndices
        then writer.WriteU4 index
        else writer.WriteU2 index

let codedIndex count n indexer = CodedIndex(count, n, indexer)

[<ReferenceEquality; NoComparison>]
type CliInfo =
    { HeaderRva: uint32
      Cli: CliMetadata
      /// Specifies the RVA and size of the CLI metadata (II.25.3.3).
      mutable Metadata: ChunkWriter
      /// Specifies the RVA and size of the "hash data for this PE file" (II.25.3.3).
      mutable StrongNameSignature: ChunkWriter
      MethodBodies: Dictionary<IMethodBody, uint32>
      StringsStream: Heap<string>
      UserStringStream: UserStringHeap
      GuidStream: Heap<Guid>
      BlobStream: SerializedBlobHeap }

/// Writes the CLI header (II.25.3.3).
let header info (writer: ChunkWriter) =
    let header = info.Cli.Header
    writer.WriteU4 Size.CliHeader
    writer.WriteU2 header.MajorRuntimeVersion
    writer.WriteU2 header.MinorRuntimeVersion

    // MetaData
    info.Metadata <- writer.CreateWriter()
    writer.SkipBytes 8u

    writer.WriteU4 info.Cli.HeaderFlags // Flags

    let entryPointTable, entryPointToken =
        match info.Cli.EntryPointToken with
        | ValueNone -> 0uy, 0u
        | ValueSome (ValidEntryPoint (Index main))
        | ValueSome (CustomEntryPoint (Index main)) ->
            0x6uy, uint32 main
        | ValueSome (EntryPointFile file) ->
            0x26uy, uint32 file

    MetadataToken.write entryPointToken entryPointTable writer

    // Resources
    writer.WriteU4 0u
    writer.WriteU4 0u

    // StrongNameSignature
    info.StrongNameSignature <- writer.CreateWriter()
    writer.SkipBytes 8u

    writer.WriteU8 0UL // CodeManagerTable

    // VTableFixups
    writer.WriteU4 0u
    writer.WriteU4 0u

    writer.WriteU8 0UL // ExportAddressTableJumps
    writer.WriteU8 0UL // ManagedNativeHeader

let methodBody content (body: IMethodBody) =
    if not body.Exists then invalidArg "body" "The method body should exist"
    let info = body.WriteBody content
    struct(content.Writer.Size, info)

let bodies rva (info: CliInfo) (writer: ChunkWriter) =
    let chunk = LinkedList<byte[]>().AddFirst(Array.zeroCreate writer.Chunk.Value.Length)
    let content = MethodBodyContent(info.Cli, info.UserStringStream)
    let mutable offset = rva

    for method in info.Cli.MethodDef.Rows do
        match info.MethodBodies.TryGetValue method.Body with
        | false, _ when method.Body.Exists ->
            content.Reset chunk
            let struct(size, body) = methodBody content method.Body
            
            // NOTE: For fat (and tiny) formats, "two least significant bits of first byte" indicate the type.
            // TODO: Add checks for no exceptions and extra data sections to generate Tiny format.
            let locals, localsi =
                match method.Body.LocalVariables with
                | ValueSome i ->
                    let signature = &info.Cli.Blobs.LocalVarSig.[info.Cli.StandAloneSig.GetSignature i]
                    signature.Length, uint32 i
                | ValueNone -> 0, 0u
            let tiny = size < 64u && body.MaxStack <= 8us && locals <= 0 && not content.ThrowsExceptions // &&
            let pos = uint32 writer.Position

            // Header
            if tiny
            then uint8 ILMethodFlags.TinyFormat ||| (byte size <<< 2) |> writer.WriteU1 // Flags and Size
            else
                let mutable flags = ILMethodFlags.FatFormat // TODO: Set other fat method flags as needed.
                if body.InitLocals then flags <- flags ||| ILMethodFlags.InitLocals
                flags ||| LanguagePrimitives.EnumOfValue(Size.FatFormat <<< 12) |> writer.WriteU2 // Flags and Size
                writer.WriteU2 body.MaxStack
                writer.WriteU4 size
                MetadataToken.write localsi 0x11uy writer // LocalVarSigTok

            // Body
            writer.WriteBytes(chunk, 0, size) |> ignore
            if not tiny then writer.AlignTo 4u

            info.MethodBodies.Item <- method.Body, offset
            offset <- offset + (uint32 writer.Position) - pos

            // TODO: Write extra method data sections.
        | false, _ -> info.MethodBodies.Item <- method.Body, 0u
        | true, _ -> ()

    writer.AlignTo 4u

/// Writes the contents of the #~ stream (II.24.2.6).
let tables (info: CliInfo) (writer: ChunkWriter) =
    writer.WriteU4 0u // Reserved
    writer.WriteU1 2uy // MajorVersion
    writer.WriteU1 0uy // MinorVersion

    do // HeapSizes
        let mutable bits = HeapSizes.None
        if info.StringsStream.IndexSize = 4 then bits <- bits ||| HeapSizes.String
        if info.GuidStream.IndexSize = 4 then bits <- bits ||| HeapSizes.Guid
        if info.BlobStream.IndexSize = 4 then bits <- bits ||| HeapSizes.Blob
        writer.WriteU1 bits

    writer.WriteU1 1uy // Reserved
    writer.WriteU8 info.Cli.Valid
    writer.WriteU8 0UL // Sorted

    let tables = info.Cli

    // Rows
    for row in tables.RowCounts do
        writer.WriteU4 row

    // Tables

    // Calculate how big indices and coded indices should be.
    let typeCount = tables.TypeDef.Count + tables.TypeRef.Count + tables.TypeSpec.Count
    let interfaceImpl = // TypeDefOrRef
        CodedIndex(typeCount, 2, fun (index: InterfaceIndex) -> uint32 index.Value, uint32 index.Tag)
    let genericParamConstraint = // TypeDefOrRef
        let indexer =
            function
            | GenericParamConstraint.AbstractClass (Index tdef)
            | GenericParamConstraint.Class (Index tdef) 
            | GenericParamConstraint.Interface (Index tdef) -> uint32 tdef, 0u
            | GenericParamConstraint.TypeRef tref -> uint32 tref, 1u
            | GenericParamConstraint.TypeSpec tspec -> uint32 tspec, 2u
        CodedIndex(typeCount, 2, indexer)
    let eventType = // TypeDefOrRef
        let indexer =
            function
            | EventType.Null -> 0u, 0u
            | EventType.AbstractClass (Index tdef)
            | EventType.ConcreteClass (Index tdef)
            | EventType.SealedClass (Index tdef) -> uint32 tdef, 0u
            | EventType.TypeRef tref -> uint32 tref, 1u
            | EventType.TypeSpec tspec -> uint32 tspec, 2u
        CodedIndex(typeCount, 2, indexer)

    let resolutionScope =
        let total =
            1 // Module
            + tables.ModuleRef.Count
            + tables.AssemblyRef.Count
            + tables.TypeRef.Count
        let indexer rscope =
            match rscope with
            | ResolutionScope.Null -> 0u, 0u
            | _ -> uint32 rscope.Value, uint32 rscope.Tag
        CodedIndex(total, 2, indexer)

    let extends =
        let indexer =
            function
            | Extends.AbstractClass (Index tdef)
            | Extends.ConcreteClass (Index tdef) -> uint32 tdef, 0u
            | Extends.TypeRef tref -> uint32 tref, 1u
            | Extends.TypeSpec tspec -> uint32 tspec, 2u
            | Extends.Null -> 0u, 0u
        CodedIndex(typeCount, 2, indexer)

    let memberRefParent =
        let total =
            tables.TypeDef.Count
            + tables.TypeRef.Count
            + tables.ModuleRef.Count
            + tables.MethodDef.Count
            + tables.TypeSpec.Count
        CodedIndex(total, 3, fun (index: MemberRefParent) -> uint32 index.Value, uint32 index.Tag)

    let constantParent = // HasConstant
        let total = tables.Field.Count + tables.Param.Count + tables.Property.Count
        CodedIndex(total, 2, fun (index: ConstantParent) -> uint32 index.Value, uint32 index.Tag)

    let customAttriuteParent = // HasCustomAttribute
        let total =
            tables.MethodDef.Count
            + tables.Field.Count
            + tables.TypeRef.Count
            + tables.TypeDef.Count
            + tables.Param.Count
            + tables.InterfaceImpl.Count
            + tables.MemberRef.Count
            + 1 // Module
            // Permission
            // Property
            // Event
            // StandAloneSig
            + tables.ModuleRef.Count
            + tables.TypeSpec.Count
            + if tables.Assembly.IsSome then 1 else 0
            + tables.AssemblyRef.Count
            + tables.File.Count
            // ExportedType
            // ManifestResource
            // GenericParam
            // GenericParamConstraint
            // MethodSpec
        let indexer (parent: CustomAttributeParent) =
            match parent.Tag with
            | CustomAttributeParentTag.TypeDef -> parent.ToRawIndex<TypeDefRow>() |> uint32, 3u
            | CustomAttributeParentTag.Assembly -> 1u, 14u
            | _ -> ArgumentOutOfRangeException("parent", parent, "Invalid custom attribute parent") |> raise
        CodedIndex(total, 5, indexer)

    let methodSemanticsAssociation = // HasSemantics
        let total = tables.Property.Count // + tables.Event.Count
        CodedIndex(total, 1, fun (index: MethodAssociation) -> uint32 index.Value, uint32 index.Tag)

    let methodDefOrRef =
        // TODO: Figure out if table count for MethodDefOrRef coded index includes all of MemberRef or just MethodRefs.
        let total = tables.MethodDef.Count + tables.MemberRef.Count
        let indexer =
            function
            | MethodDefOrRef.Def method -> uint32 method, 0u
            | MethodDefOrRef.RefDefault (Index method)
            | MethodDefOrRef.RefGeneric (Index method)
            | MethodDefOrRef.RefVarArg (Index method) -> uint32 method, 1u
        CodedIndex(total, 1, indexer)

    let customAttributeType =
        let total = tables.MethodDef.Count + tables.MemberRef.Count
        let indexer =
            function
            | CustomAttributeType.MethodRefDefault (Index mref)
            | CustomAttributeType.MethodRefGeneric (Index mref)
            | CustomAttributeType.MethodRefVarArg (Index mref) -> uint32 mref, 3u
            | CustomAttributeType.MethodDef mdef -> uint32 mdef, 2u
        CodedIndex(total, 3, indexer)

    let genericParamOwner = // TypeOrMethodDef
        let total = tables.TypeDef.Count + tables.MethodDef.Count
        CodedIndex(total, 1, fun (index: GenericParamOwner) -> uint32 index.Value, uint32 index.Tag)

    // Module (0x00)
    writer.WriteU2 0us // Generation
    info.StringsStream.WriteStringIndex(tables.Module.Name, writer)
    info.GuidStream.WriteIndex(tables.Module.Mvid, writer)
    info.GuidStream.WriteZero writer // EncId
    info.GuidStream.WriteZero writer // Encbaseid

    // TypeRef (0x01)
    for tref in tables.TypeRef.Rows do
        resolutionScope.WriteIndex(tref.ResolutionScope, writer)
        info.StringsStream.WriteStringIndex(tref.TypeName, writer)
        info.StringsStream.WriteIndex(tref.TypeNamespace, writer)

    // TypeDef (0x02)
    if tables.TypeDef.Count > 0 then
        let mutable index, field, method = 1, 1u, 1u
        for tdef in tables.TypeDef.Rows do
            writer.WriteU4 tdef.Flags
            info.StringsStream.WriteStringIndex(tdef.TypeName, writer)
            info.StringsStream.WriteIndex(tdef.TypeNamespace, writer)
            extends.WriteIndex(tdef.Extends, writer)

            let index' = RawIndex index
            index <- index + 1

            tables.Field.WriteSimpleIndex(RawIndex field, writer)
            field <- field + (tables.Field.GetCount index')

            tables.Field.WriteSimpleIndex(RawIndex method, writer)
            method <- method + (tables.MethodDef.GetCount index')

    // Field (0x04)
    for row in tables.Field.Rows do
        writer.WriteU2 row.Flags
        info.StringsStream.WriteStringIndex(row.Name, writer)
        info.BlobStream.WriteIndex(row.Signature, writer) // Signature

    // MethodDef (0x06)
    if tables.MethodDef.Count > 0 then
        let mutable param = 1u
        for i in tables.MethodDef.Indices do
            let method = &tables.MethodDef.[i]
            writer.WriteU4 info.MethodBodies.[method.Body] // Rva
            writer.WriteU2 method.ImplFlags
            writer.WriteU2 method.Flags
            info.StringsStream.WriteStringIndex(method.Name, writer)
            info.BlobStream.WriteIndex(method.Signature, writer) // Signature
            tables.Param.WriteSimpleIndex(param, writer) // ParamList
            param <- param + uint32(tables.Param.GetParameters(i).Length)

    // Param (0x08)
    for row in tables.Param.Rows do
        writer.WriteU2 row.Flags
        writer.WriteU2 row.Sequence
        info.StringsStream.WriteIndex(row.Name, writer)

    // InterfaceImpl (0x09)
    for row in tables.InterfaceImpl.Rows do
        tables.TypeDef.WriteSimpleIndex(row.Class, writer)
        interfaceImpl.WriteIndex(row.Interface, writer)

    // MemberRef (0x0A)
    for row in tables.MemberRef.Rows do
        memberRefParent.WriteIndex(row.Class, writer)
        info.StringsStream.WriteStringIndex(row.Name, writer)

        // Signature
        match row.Signature with
        | MemberRefSignature.MethodRef method -> info.BlobStream.WriteIndex(method, writer)
        | MemberRefSignature.FieldRef field -> info.BlobStream.WriteIndex(field, writer)

    // Constant (0x0B)
    for row in tables.Constant.Rows do
        let ctype =
            match row.Value with
            | ConstantBlob.Integer value ->
                match value.Tag with
                | IntegerType.Bool -> ElementType.Boolean
                | IntegerType.Char -> ElementType.Char
                | IntegerType.I1 -> ElementType.I1
                | IntegerType.U1 -> ElementType.U1
                | IntegerType.I2 -> ElementType.I2
                | IntegerType.U2 -> ElementType.U2
                | IntegerType.I4 -> ElementType.I4
                | IntegerType.U4 -> ElementType.U4
                | IntegerType.I8 -> ElementType.I8
                | IntegerType.U8 -> ElementType.U8
                | _ -> sprintf "Invalid constant integer type for parent %O" row.Parent |> invalidOp
            | ConstantBlob.String _ -> ElementType.String
            | ConstantBlob.Float FloatConstantBlob.R4 -> ElementType.R4
            | ConstantBlob.Float FloatConstantBlob.R8 -> ElementType.R8
            | ConstantBlob.Null -> ElementType.Class

        writer.WriteU1 ctype // Type
        writer.WriteU1 0uy // Padding
        constantParent.WriteIndex(row.Parent, writer)
        info.BlobStream.WriteIndex(row.Value, writer)

    // CustomAttribute (0x0C)
    for row in tables.CustomAttribute do
        customAttriuteParent.WriteIndex(row.Parent, writer)
        customAttributeType.WriteIndex(row.Type, writer)
        info.BlobStream.WriteIndex(row.Value, writer)



    // StandAloneSig (0x11)
    for locals in tables.StandAloneSig.LocalVariables do // LocalVarSig
        info.BlobStream.WriteIndex(locals, writer)
    for signature in tables.StandAloneSig.MethodSignatures do // MethodDefSig, MethodRefSig
        match signature with
        | FunctionPointer.Def mdef -> info.BlobStream.WriteIndex(mdef, writer)
        | FunctionPointer.Ref mref -> info.BlobStream.WriteIndex(mref, writer)

    // EventMap (0x12)
    if tables.EventMap.Count > 0 then
        let mutable event = 1

        // TODO: Will the order of the parent types matter for EventMap?
        for parent in tables.EventMap.Owners do
            tables.TypeDef.WriteSimpleIndex(parent, writer)
            tables.Event.WriteSimpleIndex(RawIndex event, writer) // EventList
            event <- event + int32(tables.EventMap.GetCount parent)

    // Event (0x14)
    for row in tables.Event.Rows do
        writer.WriteU2 row.Flags
        info.StringsStream.WriteStringIndex(row.Name, writer)
        eventType.WriteIndex(row.EventType, writer)

    // PropertyMap (0x15)
    if tables.PropertyMap.Count > 0 then
        let mutable property = 1

        // TODO: Will the order of the parent types matter for PropertyMap?
        for parent in tables.PropertyMap.Owners do
            tables.TypeDef.WriteSimpleIndex(parent, writer)
            tables.Property.WriteSimpleIndex(RawIndex property, writer) // PropertyList
            property <- property + int32(tables.PropertyMap.GetCount parent)

    // Property (0x17)
    for row in tables.Property.Rows do
        writer.WriteU2 row.Flags
        info.StringsStream.WriteStringIndex(row.Name, writer)
        info.BlobStream.WriteIndex(row.Type, writer)

    // MethodSemantics (0x18)
    for row in tables.MethodSemantics.Rows do
        writer.WriteU2 row.Semantics
        tables.MethodDef.WriteSimpleIndex(row.Method, writer)
        methodSemanticsAssociation.WriteIndex(row.Association, writer)

    // MethodImpl (0x19)
    for row in tables.MethodImpl do
        tables.TypeDef.WriteSimpleIndex(row.Class, writer)
        methodDefOrRef.WriteIndex(row.MethodBody, writer)
        methodDefOrRef.WriteIndex(row.MethodDeclaration, writer)

    // ModuleRef (0x1A)
    for moduleRef in tables.ModuleRef.Rows do
        info.StringsStream.WriteStringIndex(tables.File.[moduleRef.File].FileName, writer)

    // TypeSpec (0x1B)
    for typeSpec in tables.TypeSpec.Rows do info.BlobStream.WriteIndex(typeSpec.Signature, writer)




    // Assembly (0x20)
    if tables.Assembly.IsSome then
        let assembly = tables.Assembly.Value
        writer.WriteU4 0x8004u // TODO: Determine what HashAlgId should be.
        writer.WriteU2 assembly.Version.Major
        writer.WriteU2 assembly.Version.Minor
        writer.WriteU2 (max assembly.Version.Build 0)
        writer.WriteU2 (max assembly.Version.Revision 0)
        writer.WriteU4 0u // Flags // TODO: Determine what flags an assembly should have.
        info.BlobStream.WriteEmpty writer // PublicKey // TODO: Determine how to write the PublicKey into a blob.
        info.StringsStream.WriteStringIndex(assembly.Name, writer)
        info.StringsStream.WriteStringIndex(assembly.Culture, writer)

    // AssemblyRef (0x23)
    for row in tables.AssemblyRef.Rows do
        writer.WriteU2 row.Version.Major
        writer.WriteU2 row.Version.Minor
        writer.WriteU2 row.Version.Build
        writer.WriteU2 row.Version.Revision
        writer.WriteU4 row.Flags

        match row.PublicKeyOrToken.AsByteBlob() with
        | ValueSome i -> info.BlobStream.WriteIndex(i, writer)
        | ValueNone -> info.BlobStream.WriteEmpty writer

        info.StringsStream.WriteStringIndex(row.Name, writer)
        info.StringsStream.WriteStringIndex(row.Culture, writer)
        info.BlobStream.WriteEmpty writer // HashValue // TODO: Figure out how to write the HashValue into a blob.




    // File (0x26)
    for file in tables.File.Rows do
        let flags =
            if file.ContainsMetadata
            then 0u
            else 1u
        writer.WriteU4 flags
        info.StringsStream.WriteStringIndex(file.FileName, writer)
        info.BlobStream.WriteIndex(file.HashValue, writer)




    // NestedClass (0x29)
    for row in tables.NestedClass do
        tables.TypeDef.WriteSimpleIndex(row.NestedClass, writer)
        tables.TypeDef.WriteSimpleIndex(row.EnclosingClass, writer)

    // GenericParam (0x2A)
    for row in tables.GenericParam.Rows do
        writer.WriteU2 row.Number
        writer.WriteU2 row.Flags
        genericParamOwner.WriteIndex(row.Owner, writer)
        info.StringsStream.WriteStringIndex(row.Name, writer)

    // MethodSpec (0x2B)
    for methodSpec in tables.MethodSpec.Rows do
        methodDefOrRef.WriteIndex(methodSpec.Method, writer)
        info.BlobStream.WriteIndex(methodSpec.Instantiation, writer)

    // GenericParamConstraint (0x2C)
    for row in tables.GenericParamConstraint.Rows do
        tables.GenericParam.WriteSimpleIndex(row.Owner, writer)
        genericParamConstraint.WriteIndex(row.Constraint, writer)

    // TODO: Write more tables.
    ()

/// Writes the CLI metadata root (II.24.2.1) and the stream headers (II.24.2.2).
let root (info: CliInfo) (writer: ChunkWriter) =
    let version = MetadataVersion.toArray info.Cli.MetadataVersion
    writer.WriteBytes Magic.CliSignature
    writer.WriteU2 1us // MajorVersion
    writer.WriteU2 1us // MinorVersion
    writer.WriteU4 0u // Reserved
    writer.WriteU4 version.Length
    writer.WriteBytes version
    writer.WriteU2 0us // Flags

    // Streams
    let streams = writer.CreateWriter()
    writer.SkipBytes 2u

    let mutable offset = writer.Size

    // Stream headers
    let inline streamHeader (name: ImmutableArray<byte>) =
        if name.Length % 4 <> 0 then
            invalidArg (nameof name) "The length of the stream header name must be a multiple of four."
        let header = writer.CreateWriter()
        let name' = header.CreateWriter()
        name'.SkipBytes 8u
        name'.WriteBytes name
        let size = name'.Size
        writer.SkipBytes size
        offset <- offset + size
        header

    let metadata = streamHeader Magic.MetadataStream
    let strings = streamHeader Magic.StringStream
    let us =
        if info.UserStringStream.StringCount > 0
        then streamHeader Magic.UserStringStream
        else Unchecked.defaultof<ChunkWriter>
    let guid = streamHeader Magic.GuidStream
    let blob =
        if info.BlobStream.SignatureCount > 0
        then streamHeader Magic.BlobStream
        else Unchecked.defaultof<ChunkWriter>

    let inline stream (header: ChunkWriter) content =
        let heap = writer.CreateWriter()
        content heap
        heap.AlignTo 4u
        header.WriteU4 offset
        header.WriteU4 heap.Size
        let size = heap.Size
        offset <- offset + size
        writer.SkipBytes size

    // #~
    stream metadata (tables info)

    // #Strings
    stream strings (Heap.writeStrings info.StringsStream.Count info.Cli)

    // #US
    if us <> Unchecked.defaultof<ChunkWriter> then
        stream us (Heap.writeUS info.UserStringStream info.Cli)

    // #GUID
    stream guid (Heap.writeGuid info.Cli) // TODO: Fix, GUID heap should be an array of guids, where 1 refers to the first item.

    // #Blob
    if blob <> Unchecked.defaultof<ChunkWriter> then
        stream blob (Heap.writeBlob info.BlobStream info.Cli)

    do // Stream count
        let mutable count = 3u // #~, #Strings, #GUID
        if info.UserStringStream.StringCount > 0 then count <- count + 1u
        if info.BlobStream.SignatureCount > 0 then count <- count + 1u

        streams.WriteU2 count

/// Writes the entirety of the CLI metadata to the specified writer.
let metadata (cli: CliMetadata) (headerRva: uint32) (section: ChunkWriter) =
    let writer = section.CreateWriter()
    let info =
        { HeaderRva = headerRva
          Cli = cli
          Metadata = Unchecked.defaultof<ChunkWriter>
          StrongNameSignature = Unchecked.defaultof<ChunkWriter>
          MethodBodies = Dictionary<_, _> cli.MethodDef.Count
          StringsStream = Heap.strings cli
          UserStringStream = UserStringHeap 64
          GuidStream = Heap.guid cli
          BlobStream = Heap.blob cli section.Chunk.Value.Length }
    let mutable rva = headerRva

    header info writer
    rva <- rva + Size.CliHeader

    // Strong Name Signature
    if not cli.Header.StrongNameSignature.IsEmpty then
        info.StrongNameSignature.WriteU4 rva
        writer.ResetSize()
        writer.WriteBytes(cli.Header.StrongNameSignature)
        info.StrongNameSignature.WriteU4 writer.Size
        writer.AlignTo 4u
        rva <- rva + writer.Size

    // Method Bodies
    writer.ResetSize()
    bodies rva info writer
    writer.AlignTo 4u
    rva <- rva + writer.Size

    // CLI metadata
    info.Metadata.WriteU4 rva
    writer.ResetSize()
    root info writer
    info.Metadata.WriteU4 writer.Size
    rva <- rva + writer.Size

    section.SkipBytes (rva - headerRva)
