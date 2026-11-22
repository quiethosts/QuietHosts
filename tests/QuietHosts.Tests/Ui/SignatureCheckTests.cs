using System;
using System.IO;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// How the app knows, offline, whether its exe is signed (spec 2.8.18, T-A-32): an unsigned file is read from its PE
    /// header without asking Windows; a file with a signature is checked by WinVerifyTrust (cache only, no revocation);
    /// anything unexpected is Unverified when a signature blob exists and Unsigned otherwise.
    /// </summary>
    public class SignatureCheckTests
    {
        [Fact]
        public void An_unsigned_file_is_read_from_its_header_without_asking_windows()
        {
            string self = typeof(SignatureCheckTests).Assembly.Location;
            Assert.False(SignatureCheck.HasSignatureBlob(self));
            int before = SignatureCheck.VerifyCalls;
            SignatureState s = SignatureCheck.Read(self);
            Assert.Equal(SignatureKind.Unsigned, s.Kind);
            Assert.Null(s.Publisher);
            Assert.Equal(before, SignatureCheck.VerifyCalls);
        }

        [Fact]
        public void An_authenticode_signed_file_is_signed_by_its_publisher()
        {
            // xunit.assert.dll ships Authenticode-signed (Appendix A.2). If a later version ships unsigned, pick another
            // Authenticode-signed file from the test output.
            string signed = typeof(Assert).Assembly.Location;
            Assert.True(SignatureCheck.HasSignatureBlob(signed));
            int before = SignatureCheck.VerifyCalls;
            SignatureState s = SignatureCheck.Read(signed);
            Assert.Equal(before + 1, SignatureCheck.VerifyCalls);
            Assert.Equal(SignatureKind.Signed, s.Kind);
            Assert.Equal("xUnit.net (.NET Foundation)", s.Publisher);
        }

        [Fact]
        public void A_signature_windows_cant_confirm_is_unverified()
        {
            using (var dir = new TempDir())
            {
                // A copy of an unsigned file whose security directory now points at 8 bytes of nothing.
                byte[] bytes = File.ReadAllBytes(typeof(SignatureCheckTests).Assembly.Location);
                int sizeOffset = SecuritySizeOffset(bytes);
                int end = bytes.Length;
                Array.Resize(ref bytes, end + 8);
                BitConverter.GetBytes(end).CopyTo(bytes, sizeOffset - 4);
                BitConverter.GetBytes(8).CopyTo(bytes, sizeOffset);
                string fake = dir.Combine("fake.dll");
                File.WriteAllBytes(fake, bytes);
                Assert.True(SignatureCheck.HasSignatureBlob(fake));

                int before = SignatureCheck.VerifyCalls;
                SignatureState s = SignatureCheck.Read(fake);
                Assert.Equal(before + 1, SignatureCheck.VerifyCalls);
                Assert.Equal(SignatureKind.Unverified, s.Kind);
                Assert.Null(s.Publisher);
            }
        }

        [Fact]
        public void Anything_that_isnt_a_signed_pe_file_is_unsigned()
        {
            using (var dir = new TempDir())
            {
                string text = dir.Combine("notes.txt");
                File.WriteAllText(text, "MZ but not a program at all.");
                string empty = dir.Combine("empty.exe");
                File.WriteAllBytes(empty, Array.Empty<byte>());
                int before = SignatureCheck.VerifyCalls;
                foreach (string path in new[] { text, empty, dir.Combine("missing.exe") })
                {
                    SignatureState s = SignatureCheck.Read(path);
                    Assert.Equal(SignatureKind.Unsigned, s.Kind);
                    Assert.Null(s.Publisher);
                }
                Assert.Equal(before, SignatureCheck.VerifyCalls);
            }
        }

        [Theory]
        [InlineData(0x10B, 96, 0, false)]
        [InlineData(0x10B, 96, 4096, true)]
        [InlineData(0x20B, 112, 0, false)]
        [InlineData(0x20B, 112, 4096, true)]
        [InlineData(0x107, 96, 4096, false)]
        public void The_security_directory_is_found_in_pe32_and_pe32_plus_headers(int magic, int directories, int securitySize, bool expected)
        {
            using (var dir = new TempDir())
            {
                // A synthetic header: "MZ", e_lfanew at 0x3C, "PE\0\0", a 20-byte file header, then the optional header.
                var header = new byte[1024];
                header[0] = (byte)'M';
                header[1] = (byte)'Z';
                const int pe = 0x80;
                BitConverter.GetBytes(pe).CopyTo(header, 0x3C);
                header[pe] = (byte)'P';
                header[pe + 1] = (byte)'E';
                int optional = pe + 4 + 20;
                BitConverter.GetBytes((ushort)magic).CopyTo(header, optional);
                BitConverter.GetBytes(16).CopyTo(header, optional + directories - 4);
                BitConverter.GetBytes(securitySize).CopyTo(header, optional + directories + 4 * 8 + 4);
                string path = dir.Combine("synthetic.exe");
                File.WriteAllBytes(path, header);
                Assert.Equal(expected, SignatureCheck.HasSignatureBlob(path));
            }
        }

        /// <summary>Where the size of a PE file's security directory entry is (the address is the 4 bytes before it).</summary>
        private static int SecuritySizeOffset(byte[] file)
        {
            int pe = BitConverter.ToInt32(file, 0x3C);
            int optional = pe + 4 + 20;
            ushort magic = BitConverter.ToUInt16(file, optional);
            int directories = optional + (magic == 0x20B ? 112 : 96);
            return directories + 4 * 8 + 4;
        }
    }
}
