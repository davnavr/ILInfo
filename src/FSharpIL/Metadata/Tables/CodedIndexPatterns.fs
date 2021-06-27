﻿[<AutoOpen>]
module FSharpIL.Metadata.Tables.CodedIndexPatterns

let inline invalidCodedIndex tag = failwithf "Invalid coded index tag %A" tag

[<RequireQualifiedAccess>]
module TypeDefOrRef =
    let inline (|Def|Ref|Spec|) (index: TypeDefOrRef) =
        match index.Tag with
        | TypeDefOrRefTag.TypeRef -> Ref(TableIndex.ofIntUnsafe<TypeRefRow> index.Index)
        | TypeDefOrRefTag.TypeSpec -> Spec(TableIndex.ofIntUnsafe<TypeSpecRow> index.Index)
        | TypeDefOrRefTag.TypeDef
        | _ -> Def(TableIndex.ofIntUnsafe<TypeDefRow> index.Index)

    let Def ({ TableIndex = index }: TableIndex<TypeDefRow>) = TypeDefOrRef(TypeDefOrRefTag.TypeDef, index)
    let Ref ({ TableIndex = index }: TableIndex<TypeDefRow>) = TypeDefOrRef(TypeDefOrRefTag.TypeRef, index)
    //let Spec




[<RequireQualifiedAccess>]
module MemberRefParent =
    let TypeRef({ TableIndex = index }: TableIndex<TypeRefRow>) = MemberRefParent(MemberRefParentTag.TypeRef, index)




[<RequireQualifiedAccess>]
module ResolutionScope =
    let inline (|Null|TypeRef|ModuleRef|Module|AssemblyRef|) (rscope: ResolutionScope) =
        match rscope.Tag with
        | _ when rscope.IsNull -> Null
        | ResolutionScopeTag.TypeRef -> TypeRef(TableIndex.ofIntUnsafe<TypeRefRow> rscope.Index)
        | ResolutionScopeTag.ModuleRef -> ModuleRef(TableIndex.ofIntUnsafe<ModuleRefRow> rscope.Index)
        | ResolutionScopeTag.Module -> Module(TableIndex.ofIntUnsafe<ModuleRow> rscope.Index)
        | ResolutionScopeTag.AssemblyRef -> AssemblyRef(TableIndex.ofIntUnsafe<AssemblyRefRow> rscope.Index)
        | bad -> invalidCodedIndex bad

    let Null = ResolutionScope()
    let TypeRef({ TableIndex = index }: TableIndex<TypeRefRow>) = ResolutionScope(ResolutionScopeTag.TypeRef, index)
    let AssemblyRef({ TableIndex = index }: TableIndex<AssemblyRefRow>) = ResolutionScope(ResolutionScopeTag.AssemblyRef, index)

