﻿namespace FSharpIL.PortableExecutable

open System.Collections.Immutable
open System.ComponentModel

open FSharpIL.Metadata

// II.25.2.3.3
// TODO: Should data directories and section table be handled by one type instead?
// NOTE: Pointers in the DataDirectories can point to content in different parts of a section!
// NOTE: The RVA needs to be converted to/from file offset
type DataDirectories =
    { // ExportTable
      ImportTable: unit
      // ResourceTable
      // ExceptionTable
      // CertificateTable
      BaseRelocationTable: unit
      //DebugTable
      //CopyrightTable
      //GlobalPointer
      // TLSTable
      // LoadConfigTable
      // BoundImportTable
      ImportAddressTable: unit
      // DelayImportDescriptor
      // CliHeader
      // Reserved
      }

    static member Default =
        { ImportTable = ()
          BaseRelocationTable = ()
          ImportAddressTable = () }

type RawSectionData = Lazy<byte[]>

type SectionData =
    | RawData of RawSectionData
    | CliHeader of CliHeader
    // TODO: Add types for import table and import address table

[<System.Flags>]
type SectionFlags =
    | Code = 0x20u
    | InitializedData = 0x40u
    | UninitializedData = 0x80u
    | Execute = 0x20000000u
    | Read = 0x40000000u
    | Write = 0x80000000u

// II.25.3
/// NOTE: Section headers begin after the file headers, but must account for SizeOfHeaders, which is rounded up to a multiple of FileAlignment.
type SectionHeader =
    { SectionName: SectionName
      // VirtualSize: uint32
      // VirtualAddress: uint32
      Data: ImmutableArray<SectionData>
      //PointerToRelocations: uint32
      //PointerToLineNumbers: uint32
      //NumberOfRelocations: uint16
      //NumberOfLineNumbers: uint16
      Characteristics: SectionFlags
      }

[<RequireQualifiedAccess>]
module SectionInfo =
    [<EditorBrowsable(EditorBrowsableState.Never)>]
    type Info =
        private
            { Data: DataDirectories
              Sections: ImmutableArray<SectionHeader> }

        member this.DataDirectories = this.Data
        member this.SectionTable = this.Sections

type SectionInfo = SectionInfo.Info