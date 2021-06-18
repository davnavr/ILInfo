﻿namespace FSharpIL.Writing.Abstractions

open System.Runtime.CompilerServices

open FSharpIL.Metadata.Tables

[<IsReadOnly>]
type MemberOwner<'Kind when 'Kind : struct> = struct
    val Kind: 'Kind
    val Index: TypeEntryIndex<unit>
    internal new (kind, index) = { Kind = kind; Index = index }
end

[<RequireQualifiedAccess>]
module MemberOwnerKinds =
    [<IsReadOnly; Struct>]
    [<RequireQualifiedAccess>]
    type InstanceMember =
        | ConcreteClass
        | AbstractClass
        | SealedClass
        | ValueType

    [<IsReadOnly; Struct>]
    [<RequireQualifiedAccess>]
    type AbstractMember =
        | AbstractClass
        | Interface

    [<IsReadOnly; Struct>]
    [<RequireQualifiedAccess>]
    type StaticMember =
        | ConcreteClass
        | AbstractClass
        | SealedClass
        | StaticClass
        | ValueType

/// Represents a type that can own an instance field, an object constructor, or a non-abstract instance method, property, or
/// event.
type InstanceMemberOwner = MemberOwner<MemberOwnerKinds.InstanceMember>

/// Represents a type that can own an abstract method, property, or event.
type AbstractMemberOwner = MemberOwner<MemberOwnerKinds.AbstractMember>

/// Represents a type that can own a static field, method, property, event, or class constructor.
type StaticMemberOwner = MemberOwner<MemberOwnerKinds.StaticMember>

[<RequireQualifiedAccess>]
module StaticMemberOwner =
    let ConcreteClass (index: TypeEntryIndex<ConcreteClassDef>) =
        StaticMemberOwner(MemberOwnerKinds.StaticMember.ConcreteClass, TypeEntryIndex.removeTag index)
    //let inline (|ConcreteClass|AbstractClass|SealedClass|ValueType|) (owner: ClassConstructorOwner) =
    //    match owner.Kind with
    //    | MemberOwnerKinds.ClassConstructor.ConcreteClass 

[<AutoOpen>]
module MemberOwner =
    let inline (|MemberOwner|) (owner: MemberOwner<_>) = owner.Index
    //let (|ClassConstructorOwner|)
