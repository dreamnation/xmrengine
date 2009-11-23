// ******************************************************************
// Copyright (c) 2008, 2009 Melanie Thielker
//
// All rights reserved
//

using System;

namespace Xumeo
{
    public class Protect
    {
        public Byte[] GetHash()
        {
            HardwareInfo hwi = new HardwareInfo();

//            Console.WriteLine("CPU ID {0}", hwi.GetCPUId());
//            Console.WriteLine("MAC {0}", hwi.GetMACAddress());

            System.Security.Cryptography.MD5CryptoServiceProvider md5 =
                    new System.Security.Cryptography.MD5CryptoServiceProvider();

            Byte[] message = System.Text.Encoding.ASCII.GetBytes(hwi.GetCPUId() + hwi.GetMACAddress());
            Byte[] hash = md5.ComputeHash(message);

            return hash;
        }

        public Byte[] Revolve(Byte[] input, string key)
        {
            if(input.Length != 16)
            {
                Console.WriteLine("Bad input to Revolve");
                return new Byte[0];
            }

            Byte[] L = new Byte[8];
            Byte[] R = new Byte[8];

            Array.Copy(input, 0, L, 0, 8);
            Array.Copy(input, 8, R, 0, 8);

            System.Security.Cryptography.MD5CryptoServiceProvider md5 =
                    new System.Security.Cryptography.MD5CryptoServiceProvider();
            
            Byte[] keyraw = System.Text.Encoding.ASCII.GetBytes(key);
            Byte[] keybytes = md5.ComputeHash(keyraw);

            int i;

            for(i=0;i<8;i++)
                L[i] = (Byte)(L[i] ^ keybytes[i]);
            
            Byte[] output = new Byte[16];
            Array.Copy(R, 0, output, 0, 8);
            Array.Copy(L, 0, output, 8, 8);

            return output;
        }

    }
}
