using System.Text;

namespace Emerde.Core;

internal static class DouyinWebSignature
{
    private const string WindowEnvironment = "1920|1080|1920|1040|0|30|0|0|1872|92|1920|1040|1857|92|1|24|Win32";

    public static string CreateABogus(string query, string userAgent)
    {
        return ResultEncrypt(GenerateRandomString() + GenerateRc4Body(query, userAgent, WindowEnvironment), "s4") + "=";
    }

    private static string Rc4Encrypt(string plaintext, string key)
    {
        int[] state = Enumerable.Range(0, 256).ToArray();
        int j = 0;

        for (int i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) % 256;
            (state[i], state[j]) = (state[j], state[i]);
        }

        int x = 0;
        j = 0;
        StringBuilder result = new(plaintext.Length);
        foreach (char item in plaintext)
        {
            x = (x + 1) % 256;
            j = (j + state[x]) % 256;
            (state[x], state[j]) = (state[j], state[x]);
            int t = (state[x] + state[j]) % 256;
            result.Append((char)(state[t] ^ item));
        }

        return result.ToString();
    }

    private static string ResultEncrypt(string value, string tableName)
    {
        Dictionary<string, string> tables = new(StringComparer.Ordinal)
        {
            ["s0"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=",
            ["s1"] = "Dkdpgh4ZKsQB80/Mfvw36XI1R25+WUAlEi7NLboqYTOPuzmFjJnryx9HVGcaStCe=",
            ["s2"] = "Dkdpgh4ZKsQB80/Mfvw36XI1R25-WUAlEi7NLboqYTOPuzmFjJnryx9HVGcaStCe=",
            ["s3"] = "ckdp1h4ZKsUB80/Mfvw36XIgR25+WQAlEi7NLboqYTOPuzmFjJnryx9HVGDaStCe",
            ["s4"] = "Dkdpgh2ZmsQB80/MfvV36XI1R45-WUAlEixNLwoqYTOPuzKFjJnry79HbGcaStCe",
        };
        int[] masks = [16515072, 258048, 4032, 63];
        int[] shifts = [18, 12, 6, 0];
        string table = tables[tableName];
        int round = 0;
        int longInt = GetLongInt(round, value);
        int totalChars = (int)Math.Ceiling(value.Length / 3d * 4d);
        StringBuilder result = new(totalChars);

        for (int i = 0; i < totalChars; i++)
        {
            if (i / 4 != round)
            {
                round++;
                longInt = GetLongInt(round, value);
            }

            int index = i % 4;
            int charIndex = (longInt & masks[index]) >> shifts[index];
            result.Append(table[charIndex]);
        }

        return result.ToString();
    }

    private static int GetLongInt(int round, string value)
    {
        int offset = round * 3;
        int char1 = offset < value.Length ? value[offset] : 0;
        int char2 = offset + 1 < value.Length ? value[offset + 1] : 0;
        int char3 = offset + 2 < value.Length ? value[offset + 2] : 0;
        return (char1 << 16) | (char2 << 8) | char3;
    }

    private static int[] GenerateRandomBytes(int randomNumber, int[] option)
    {
        int byte1 = randomNumber & 255;
        int byte2 = (randomNumber >> 8) & 255;
        return
        [
            (byte1 & 170) | (option[0] & 85),
            (byte1 & 85) | (option[0] & 170),
            (byte2 & 170) | (option[1] & 85),
            (byte2 & 85) | (option[1] & 170),
        ];
    }

    private static string GenerateRandomString()
    {
        double[] randomValues = [0.123456789d, 0.987654321d, 0.555555555d];
        List<int> bytes = [];
        bytes.AddRange(GenerateRandomBytes((int)(randomValues[0] * 10000), [3, 45]));
        bytes.AddRange(GenerateRandomBytes((int)(randomValues[1] * 10000), [1, 0]));
        bytes.AddRange(GenerateRandomBytes((int)(randomValues[2] * 10000), [1, 5]));
        return new string(bytes.Select(item => (char)item).ToArray());
    }

    private static string GenerateRc4Body(string query, string userAgent, string windowEnvironment, string suffix = "cus")
    {
        Sm3 sm3 = new();
        long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] queryHash = sm3.Sum(sm3.Sum(query + suffix));
        byte[] suffixHash = sm3.Sum(sm3.Sum(suffix));
        string uaKey = new([chr(0), chr(1), chr(14)]);
        byte[] userAgentHash = sm3.Sum(ResultEncrypt(Rc4Encrypt(userAgent, uaKey), "s3"));
        long endTime = startTime + 100;

        int[] b = new int[73];
        b[8] = 3;
        b[10] = (int)endTime;
        b[16] = (int)startTime;
        b[18] = 44;
        b[19] = 1;

        int[] startTimeBytes = SplitToBytes(startTime);
        b[20] = startTimeBytes[0];
        b[21] = startTimeBytes[1];
        b[22] = startTimeBytes[2];
        b[23] = startTimeBytes[3];
        b[24] = (int)(startTime / 256 / 256 / 256 / 256) & 255;
        b[25] = (int)(startTime / 256 / 256 / 256 / 256 / 256) & 255;

        int[] arg0Bytes = SplitToBytes(0);
        b[26] = arg0Bytes[0];
        b[27] = arg0Bytes[1];
        b[28] = arg0Bytes[2];
        b[29] = arg0Bytes[3];

        b[30] = 0;
        b[31] = 1;
        int[] arg1Bytes = SplitToBytes(1);
        b[32] = arg1Bytes[0];
        b[33] = arg1Bytes[1];

        int[] arg2Bytes = SplitToBytes(14);
        b[34] = arg2Bytes[0];
        b[35] = arg2Bytes[1];
        b[36] = arg2Bytes[2];
        b[37] = arg2Bytes[3];

        b[38] = queryHash[21];
        b[39] = queryHash[22];
        b[40] = suffixHash[21];
        b[41] = suffixHash[22];
        b[42] = userAgentHash[23];
        b[43] = userAgentHash[24];

        int[] endTimeBytes = SplitToBytes(endTime);
        b[44] = endTimeBytes[0];
        b[45] = endTimeBytes[1];
        b[46] = endTimeBytes[2];
        b[47] = endTimeBytes[3];
        b[48] = b[8];
        b[49] = (int)(endTime / 256 / 256 / 256 / 256) & 255;
        b[50] = (int)(endTime / 256 / 256 / 256 / 256 / 256) & 255;

        const int pageId = 110624;
        const int aid = 6383;
        b[51] = pageId;
        int[] pageIdBytes = SplitToBytes(pageId);
        b[52] = pageIdBytes[0];
        b[53] = pageIdBytes[1];
        b[54] = pageIdBytes[2];
        b[55] = pageIdBytes[3];
        b[56] = aid;
        b[57] = aid & 255;
        b[58] = (aid >> 8) & 255;
        b[59] = (aid >> 16) & 255;
        b[60] = (aid >> 24) & 255;

        int[] windowEnvList = windowEnvironment.Select(item => (int)item).ToArray();
        b[64] = windowEnvList.Length;
        b[65] = b[64] & 255;
        b[66] = (b[64] >> 8) & 255;
        b[69] = 0;
        b[70] = 0;
        b[71] = 0;

        b[72] = b[18] ^ b[20] ^ b[26] ^ b[30] ^ b[38] ^ b[40] ^ b[42] ^ b[21] ^ b[27] ^ b[31]
            ^ b[35] ^ b[39] ^ b[41] ^ b[43] ^ b[22] ^ b[28] ^ b[32] ^ b[36] ^ b[23] ^ b[29]
            ^ b[33] ^ b[37] ^ b[44] ^ b[45] ^ b[46] ^ b[47] ^ b[48] ^ b[49] ^ b[50] ^ b[24]
            ^ b[25] ^ b[52] ^ b[53] ^ b[54] ^ b[55] ^ b[57] ^ b[58] ^ b[59] ^ b[60] ^ b[65]
            ^ b[66] ^ b[70] ^ b[71];

        List<int> body =
        [
            b[18], b[20], b[52], b[26], b[30], b[34], b[58], b[38], b[40], b[53], b[42], b[21],
            b[27], b[54], b[55], b[31], b[35], b[57], b[39], b[41], b[43], b[22], b[28], b[32],
            b[60], b[36], b[23], b[29], b[33], b[37], b[44], b[45], b[59], b[46], b[47], b[48],
            b[49], b[50], b[24], b[25], b[65], b[66], b[70], b[71],
        ];
        body.AddRange(windowEnvList);
        body.Add(b[72]);

        return Rc4Encrypt(new string(body.Select(item => (char)item).ToArray()), chr(121).ToString());
    }

    private static int[] SplitToBytes(long value)
    {
        return
        [
            (int)(value >> 24) & 255,
            (int)(value >> 16) & 255,
            (int)(value >> 8) & 255,
            (int)value & 255,
        ];
    }

    private static char chr(int value)
    {
        return (char)value;
    }

    private sealed class Sm3
    {
        private readonly List<byte> chunk = [];
        private uint[] registers = [];
        private long size;

        public Sm3()
        {
            Reset();
        }

        public byte[] Sum(string data)
        {
            Reset();
            Write(Encoding.UTF8.GetBytes(data));
            return Finish();
        }

        public byte[] Sum(byte[] data)
        {
            Reset();
            Write(data);
            return Finish();
        }

        private void Reset()
        {
            registers =
            [
                1937774191u, 1226093241u, 388252375u, 3666478592u,
                2842636476u, 372324522u, 3817729613u, 2969243214u,
            ];
            chunk.Clear();
            size = 0;
        }

        private void Write(IReadOnlyList<byte> data)
        {
            size += data.Count;
            int fill = 64 - chunk.Count;

            if (data.Count < fill)
            {
                chunk.AddRange(data);
                return;
            }

            chunk.AddRange(data.Take(fill));
            int offset = fill;
            while (chunk.Count >= 64)
            {
                Compress(chunk);
                chunk.Clear();
                if (offset < data.Count)
                {
                    int count = Math.Min(64, data.Count - offset);
                    for (int i = 0; i < count; i++)
                    {
                        chunk.Add(data[offset + i]);
                    }
                }
                offset += 64;
            }
        }

        private byte[] Finish()
        {
            Fill();
            for (int offset = 0; offset < chunk.Count; offset += 64)
            {
                Compress(chunk.Skip(offset).Take(64).ToArray());
            }

            byte[] result = new byte[32];
            for (int i = 0; i < registers.Length; i++)
            {
                uint value = registers[i];
                result[i * 4] = (byte)((value >> 24) & 255);
                result[(i * 4) + 1] = (byte)((value >> 16) & 255);
                result[(i * 4) + 2] = (byte)((value >> 8) & 255);
                result[(i * 4) + 3] = (byte)(value & 255);
            }

            Reset();
            return result;
        }

        private void Fill()
        {
            long bitLength = 8 * size;
            int paddingPosition = chunk.Count;
            chunk.Add(0x80);
            paddingPosition = (paddingPosition + 1) % 64;
            if (64 - paddingPosition < 8)
            {
                paddingPosition -= 64;
            }

            while (paddingPosition < 56)
            {
                chunk.Add(0);
                paddingPosition++;
            }

            long highBits = bitLength / 4294967296L;
            for (int i = 0; i < 4; i++)
            {
                chunk.Add((byte)((highBits >> (8 * (3 - i))) & 255));
            }

            for (int i = 0; i < 4; i++)
            {
                chunk.Add((byte)((bitLength >> (8 * (3 - i))) & 255));
            }
        }

        private void Compress(IReadOnlyList<byte> data)
        {
            uint[] w = new uint[132];
            for (int t = 0; t < 16; t++)
            {
                w[t] = ((uint)data[4 * t] << 24)
                    | ((uint)data[(4 * t) + 1] << 16)
                    | ((uint)data[(4 * t) + 2] << 8)
                    | data[(4 * t) + 3];
            }

            for (int j = 16; j < 68; j++)
            {
                uint a = w[j - 16] ^ w[j - 9] ^ LeftRotate(w[j - 3], 15);
                a = a ^ LeftRotate(a, 15) ^ LeftRotate(a, 23);
                w[j] = a ^ LeftRotate(w[j - 13], 7) ^ w[j - 6];
            }

            for (int j = 0; j < 64; j++)
            {
                w[j + 68] = w[j] ^ w[j + 4];
            }

            uint aRegister = registers[0];
            uint bRegister = registers[1];
            uint cRegister = registers[2];
            uint dRegister = registers[3];
            uint eRegister = registers[4];
            uint fRegister = registers[5];
            uint gRegister = registers[6];
            uint hRegister = registers[7];

            unchecked
            {
                for (int j = 0; j < 64; j++)
                {
                    uint ss1 = LeftRotate(LeftRotate(aRegister, 12) + eRegister + LeftRotate(GetT(j), j), 7);
                    uint ss2 = ss1 ^ LeftRotate(aRegister, 12);
                    uint tt1 = FF(j, aRegister, bRegister, cRegister) + dRegister + ss2 + w[j + 68];
                    uint tt2 = GG(j, eRegister, fRegister, gRegister) + hRegister + ss1 + w[j];

                    dRegister = cRegister;
                    cRegister = LeftRotate(bRegister, 9);
                    bRegister = aRegister;
                    aRegister = tt1;
                    hRegister = gRegister;
                    gRegister = LeftRotate(fRegister, 19);
                    fRegister = eRegister;
                    eRegister = tt2 ^ LeftRotate(tt2, 9) ^ LeftRotate(tt2, 17);
                }

                registers[0] ^= aRegister;
                registers[1] ^= bRegister;
                registers[2] ^= cRegister;
                registers[3] ^= dRegister;
                registers[4] ^= eRegister;
                registers[5] ^= fRegister;
                registers[6] ^= gRegister;
                registers[7] ^= hRegister;
            }
        }

        private static uint LeftRotate(uint value, int count)
        {
            count %= 32;
            return (value << count) | (value >> (32 - count));
        }

        private static uint GetT(int j)
        {
            return j < 16 ? 2043430169u : 2055708042u;
        }

        private static uint FF(int j, uint x, uint y, uint z)
        {
            return j < 16 ? x ^ y ^ z : (x & y) | (x & z) | (y & z);
        }

        private static uint GG(int j, uint x, uint y, uint z)
        {
            return j < 16 ? x ^ y ^ z : (x & y) | (~x & z);
        }
    }
}
