﻿/*
 *  Copyright 2017 Perfare - https://github.com/Perfare/Il2CppDumper
 *  Copyright 2017 Katy Coe - http://www.hearthcode.org - http://www.djkaty.com
 *  Copyright 2017 niico - https://github.com/pogosandbox/Il2CppDumper 
 *
 *  All rights reserved.
*/

using System;
using System.IO;
using System.Linq;

namespace Il2CppInspector.Readers
{
    internal class ElfReader : FileFormatReader<ElfReader>
    {
        private program_header_table[] program_table_element;
        private elf_header elf_header;

        public ElfReader(Stream stream) : base(stream) { }

        public override string Arch {
            get {
                switch (elf_header.e_machine) {
                    case 0x03:
                        return "x86";
                    case 0x28:
                        return "ARM";
                    default:
                        return "Unsupported";
                }
            }
        }

        protected override bool Init() {
            elf_header = ReadObject<elf_header>();

            if (elf_header.m_dwFormat != 0x464c457f) {
                // Not an ELF file
                return false;
            }
            if (elf_header.m_arch == 2)//64
            {
                // 64-bit not supported
                return false;
            }
            program_table_element = ReadArray<program_header_table>(elf_header.e_phoff, elf_header.e_phnum);
            return true;
        }

        public override long[] GetSearchLocations() {
            // Find dynamic section
            var dynamic = new elf_32_shdr();
            var PT_DYNAMIC = program_table_element.First(x => x.p_type == 2u);
            dynamic.sh_offset = PT_DYNAMIC.p_offset;
            dynamic.sh_size = PT_DYNAMIC.p_filesz;

            // We need GOT, INIT_ARRAY and INIT_ARRAYSZ
            uint _GLOBAL_OFFSET_TABLE_ = 0;
            var init_array = new elf_32_shdr();
            Position = dynamic.sh_offset;
            var dynamicend = dynamic.sh_offset + dynamic.sh_size;
            while (Position < dynamicend) {
                var tag = ReadInt32();
                if (tag == 3) //DT_PLTGOT
                {
                    _GLOBAL_OFFSET_TABLE_ = ReadUInt32();
                    continue;
                }
                else if (tag == 25) //DT_INIT_ARRAY
                {
                    init_array.sh_offset = (uint)MapVATR(ReadUInt32());
                    continue;
                }
                else if (tag == 27) //DT_INIT_ARRAYSZ
                {
                    init_array.sh_size = ReadUInt32();
                    continue;
                }
                Position += 4;
            }
            if (_GLOBAL_OFFSET_TABLE_ == 0)
                throw new InvalidOperationException("Unable to get GLOBAL_OFFSET_TABLE from PT_DYNAMIC");
            GlobalOffset = _GLOBAL_OFFSET_TABLE_;
            var locations = ReadArray<uint>(init_array.sh_offset, (int)init_array.sh_size / 4);
            return locations.Select(l => (long)l).ToArray();
        }
        
        public override long MapVATR(long uiAddr)
        {
            var program_header_table = program_table_element.First(x => uiAddr >= x.p_vaddr && uiAddr <= (x.p_vaddr + x.p_memsz));
            return uiAddr - (program_header_table.p_vaddr - program_header_table.p_offset);
        }

        private uint decodeMovImm32(byte[] asm)
        {
            ushort low = (ushort)(asm[2] + ((asm[3] & 0x70) << 4) + ((asm[1] & 0x04) << 9) + ((asm[0] & 0x0f) << 12));
            ushort high = (ushort)(asm[6] + ((asm[7] & 0x70) << 4) + ((asm[5] & 0x04) << 9) + ((asm[4] & 0x0f) << 12));
            return (uint)((high << 16) + low);
        }
    }
}
