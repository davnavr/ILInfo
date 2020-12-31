﻿/// Contains various magic numbers and bytes used throught a PE file.
module FSharpIL.Magic

let DosStub =
    let lfanew = [| 0x80; 0x00; 0x00; 0x00 |]
    [|
        0x4d; 0x5a; 0x90; 0x00; 0x03; 0x00; 0x00; 0x00
        0x04; 0x00; 0x00; 0x00; 0xFF; 0xFF; 0x00; 0x00;
        0xb8; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
        0x40; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
        0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
        0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
        0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
        0x00; 0x00; 0x00; 0x00; yield! lfanew
        0x0e; 0x1f; 0xba; 0x0e; 0x00; 0xb4; 0x09; 0xcd;
        0x21; 0xb8; 0x01; 0x4c; 0xcd; 0x21; 0x54; 0x68;
        0x69; 0x73; 0x20; 0x70; 0x72; 0x6f; 0x67; 0x72;
        0x61; 0x6d; 0x20; 0x63; 0x61; 0x6e; 0x6e; 0x6f;
        0x74; 0x20; 0x62; 0x65; 0x20; 0x72; 0x75; 0x6e;
        0x20; 0x69; 0x6e; 0x20; 0x44; 0x4f; 0x53; 0x20;
        0x6d; 0x6f; 0x64; 0x65; 0x2e; 0x0d; 0x0d; 0x0a;
        0x24; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
    |]
    |> Array.map byte

let PESignature = "PE\000\000"B

/// Magic number used in the optional header that identifies the file as a PE32 executable.
[<Literal>]
let PE32 = 0x10Bus

/// The signature of the CLI metadata root.
let CliSignature = [| 0x42uy; 0x53uy; 0x4Auy; 0x42uy; |]
