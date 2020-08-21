﻿/// Contains various magic numbers
[<RequireQualifiedAccess>]
module internal FSharpIL.Magic

open FSharpIL.Utilities

// II.25.2.1
let DOSHeader = bytes {
        'M'; 'Z'; 0x90; 0x00; 0x03; 0x00; 0x00; 0x00; 0x04; 0x00; 0x00; 0x00; 0xff; 0xff; 0x00; 0x00;
        0xb8; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x40; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
        0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
        0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; (* lfanew is here *)
    }
let DOSStub = bytes {
        0x0e; 0x1f; 0xba; 0x0e; 0x00; 0xb4; 0x09; 0xcd; 0x21; 0xb8; 0x01; 0x4c; 0xcd; 0x21; 'T'; 'h';
        'i'; 's'; ' '; 'p'; 'r'; 'o'; 'g'; 'r'; 'a'; 'm'; ' '; 'c'; 'a'; 'n'; 'n'; 'o';
        't'; ' '; 'b'; 'e'; ' '; 'r'; 'u'; 'n'; ' '; 'i'; 'n'; ' '; 'D'; 'O'; 'S'; ' ';
        'm'; 'o'; 'd'; 'e'; 0x2e; 0x0d; 0x0d; 0x0a; 0x24; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00;
    }
