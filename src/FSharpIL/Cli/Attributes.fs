﻿namespace FSharpIL.Cli

open System.Runtime.CompilerServices

open Microsoft.FSharp.Core.LanguagePrimitives

open FSharpIL.Metadata.Tables

type IBitwiseOperand<'T when 'T : struct> = interface
    abstract Or: 'T * 'T -> 'T
end

type IAttributeTag<'Flags> = interface
    abstract member RequiredFlags : 'Flags with get
end

[<IsReadOnly; Struct>]
[<StructuralComparison; StructuralEquality>]
type Attributes<'Tag, 'Flags, 'Op, 'Num
    when 'Flags : enum<'Num>
    and 'Tag :> IAttributeTag<'Flags>
    and 'Tag : struct
    and 'Op :> IBitwiseOperand<'Num>
    and 'Op : struct
    and 'Num : struct>
    internal (flags: 'Flags)
    =
    member _.Flags = flags

    override _.ToString() = flags.ToString()

    static member inline private BitwiseOr(left: 'Flags, right: 'Flags): 'Flags =
        EnumOfValue(Unchecked.defaultof<'Op>.Or(EnumToValue left, EnumToValue right))

    static member op_Implicit(flags: Attributes<'Tag, 'Flags, 'Op, 'Num>) =
        EnumToValue<_, 'Num>(Attributes<'Tag, 'Flags, 'Op, 'Num>.BitwiseOr(flags.Flags, Unchecked.defaultof<'Tag>.RequiredFlags))

    static member (|||) (left: Attributes<'Tag, 'Flags, 'Op, 'Num>, right: Attributes<'Tag, 'Flags, 'Op, 'Num>) =
        Attributes<'Tag, 'Flags, 'Op, 'Num>(Attributes<'Tag, 'Flags, 'Op, 'Num>.BitwiseOr(left.Flags, right.Flags))

[<RequireQualifiedAccess>]
module AttributeKinds =
    type U2 = struct
        interface IBitwiseOperand<uint16> with
            [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
            member _.Or(left, right) = left ||| right
    end

    type U4 = struct
        interface IBitwiseOperand<uint32> with
            [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
            member _.Or(left, right) = left ||| right
    end

type TypeAttributes<'Tag when 'Tag :> IAttributeTag<TypeDefFlags> and 'Tag : struct> =
    Attributes<'Tag, TypeDefFlags, AttributeKinds.U4, uint32>

type MethodAttributes<'Tag when 'Tag :> IAttributeTag<MethodDefFlags> and 'Tag : struct> =
    Attributes<'Tag, MethodDefFlags, AttributeKinds.U2, uint16>

[<AutoOpen>]
module Attribute =
    let (|Attributes|) flags = Attributes.op_Implicit flags

[<RequireQualifiedAccess>]
module TypeAttributes =
    type Tag = IAttributeTag<TypeDefFlags>

    type IHasStaticMethods = interface end

    let BeforeFieldInit<'Tag when 'Tag :> Tag and 'Tag : struct and 'Tag :> IHasStaticMethods> =
        TypeAttributes<'Tag> TypeDefFlags.BeforeFieldInit

    type IHasLayout = interface end

    let SequentialLayout<'Tag when 'Tag :> Tag and 'Tag : struct and 'Tag :> IHasLayout> =
        TypeAttributes<'Tag> TypeDefFlags.SequentialLayout

    let ExplicitLayout<'Tag when 'Tag :> Tag and 'Tag : struct and 'Tag :> IHasLayout> =
        TypeAttributes<'Tag> TypeDefFlags.ExplicitLayout

    type IHasStringFormat = interface end

    let UnicodeClass<'Tag when 'Tag :> Tag and 'Tag : struct and 'Tag :> IHasStringFormat> =
        TypeAttributes<'Tag> TypeDefFlags.UnicodeClass

    let AutoClass<'Tag when 'Tag :> Tag and 'Tag : struct and 'Tag :> IHasStringFormat> =
        TypeAttributes<'Tag> TypeDefFlags.AutoClass

    type ISerializableType = interface end

    let Serializable<'Tag when 'Tag :> Tag and 'Tag : struct and 'Tag :> ISerializableType> =
        TypeAttributes<'Tag> TypeDefFlags.Serializable

[<RequireQualifiedAccess>]
module MethodAttributes =
    type Tag = IAttributeTag<MethodDefFlags>
    let HideBySig<'Tag when 'Tag :> Tag and 'Tag : struct> =
        MethodAttributes<'Tag> MethodDefFlags.HideBySig
