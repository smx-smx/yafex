#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using Yafex.Support;

using System;
using System.IO;
using System.Security.Cryptography;

namespace Yafex.FileFormats
{
    enum EpkBuildType
    {
        Release,
        Debug,
        Test,
        Unknown
    }

    public enum EpkInternalFileType
    {
        Epk,
        EpkV2,
        EpkV3,
        EpkV3New,
        PakV2,
        Raw
    }

    public abstract class EpkFile
    {
        public const int SIGNATURE_SIZE = 0x80;

        protected EpkFile(Config config)
        {

        }

        private RSA FindPublicKey(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature/*, out int signedSize*/)
        {
            foreach (string file in Directory.EnumerateFiles("foo"))
            {
                byte[] key = File.ReadAllBytes(file);

                RSA rsa = RSA.Create();
                rsa.ImportRSAPublicKey(key, out int bytesRead);

                //for(int curSize = data.Length; curSize > 0; curSize--){
                if (rsa.VerifyHash(data, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
                {
                    //signedSize = curSize;
                    return rsa;
                }
                //}
            }

            //signedSize = 0;
            return null;
        }
    }
}
