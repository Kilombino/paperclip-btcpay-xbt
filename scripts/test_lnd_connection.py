"""Offline helper regression tests: no node, credentials or funds required."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

class ConnectionTests(unittest.TestCase):
    def test_setup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cert = root / "tls.cert"
            subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes",
                            "-keyout", str(root / "key"), "-out", str(cert), "-days", "1",
                            "-subj", "/CN=localhost"], check=True, capture_output=True)
            mock = root / "lncli"
            mock.write_text("""#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
p=Path(os.environ['CALLS'])
with p.open('a') as f: f.write(json.dumps(sys.argv[1:])+'\\n')
if sys.argv[1]=='getinfo':
 print(json.dumps({'features':{'512':{'is_required':True},'515':{}},'chains':[{'chain':'bitcoin','network':'mainnet'}],'synced_to_chain':True,'identity_pubkey':'02'+'11'*32}))
elif sys.argv[1]=='listmacaroonids': print('{"root_key_ids":["0","48"]}')
else: print('aabbcc')
""")
            mock.chmod(0o700)
            calls = root / "calls"
            env = os.environ | {"LNCLI": str(mock), "LND_REST": "https://localhost:8080",
                                "LND_TLS_CERT": str(cert), "CALLS": str(calls), "ROOT_KEY_ID": ""}
            def run(extra):
                calls.write_text("")
                result = subprocess.run(["sh", str(Path(__file__).with_name("lnd-connection.sh"))],
                                        env=env | extra, capture_output=True, text=True)
                return result, [json.loads(line) for line in calls.read_text().splitlines()]
            for path in [root / "missing", root / "invalid"]:
                if path.name == "invalid": path.write_text("not a certificate")
                result, log = run({"LND_TLS_CERT": str(path)})
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(log, [])
                self.assertEqual(result.stdout, "")
            for value in ["0", "48", "-1", str(2**64), "abc"]:
                result, log = run({"ROOT_KEY_ID": value})
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(any(row[0] == "bakemacaroon" for row in log))
            chosen = []
            for extra in [{}, {}, {"ROOT_KEY_ID": "49"}]:
                result, log = run(extra)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn("certthumbprint=", result.stdout)
                bake = next(row for row in log if row[0] == "bakemacaroon")
                self.assertEqual(bake[3:], ["info:read", "invoices:read", "invoices:write"])
                chosen.append(int(bake[2]))
            self.assertEqual(chosen[-1], 49)
            self.assertEqual(len(set(chosen)), 3)
            self.assertTrue(all(0 < value < 2**64 and value != 48 for value in chosen))

if __name__ == "__main__":
    unittest.main()
