import unittest
from unittest.mock import patch
import gateway as g

class GatewayTests(unittest.TestCase):
    def setUp(self):
        self.node_patch = patch.object(g, 'EXPECTED_NODE', '02'+'11'*32)
        self.node_patch.start()
        self.addCleanup(self.node_patch.stop)
    def test_missing_node_configuration_fails_closed(self):
        with patch.object(g, 'EXPECTED_NODE', ''):
            self.assertFalse(g.compatible(self.info()))
    def info(self):
        return {'id':g.EXPECTED_NODE,'network':'bitcoin','our_features':{
            'init':hex((1<<512)|(1<<515))[2:], 'node':hex((1<<512)|(1<<515))[2:], 'invoice':hex(1<<512)[2:]}}
    def test_chain_identity(self):
        info=self.info()
        self.assertTrue(g.compatible(info))
        for field in ('init','node','invoice'):
            other=self.info();other['our_features'][field]='00'
            self.assertFalse(g.compatible(other))
        info['id']='other';self.assertFalse(g.compatible(info))
    def test_spending_and_secrets_blocked(self):
        with patch.object(g,'rpc') as rpc:
            for method in ('withdraw','pay','fundchannel','close','newaddr','stop','exposesecret','createrune'):
                with self.assertRaises(ValueError):g.handle(method,[])
            rpc.assert_not_called()
    def test_only_own_invoices_returned(self):
        with patch.object(g,'rpc',return_value={'invoices':[{'label':g.PREFIX+'abc','status':'unpaid'},{'label':'unrelated'}]}):
            self.assertEqual(g.handle('listinvoices',[]),{'invoices':[{'label':'abc','status':'unpaid'}]})
    def test_cancel_cannot_delete_paid_invoice(self):
        with self.assertRaises(ValueError):g.handle('delinvoice',['abc','paid'])
    def test_creation_requires_compatible_node(self):
        with patch.object(g,'rpc',return_value={'id':'wrong'}):
            with self.assertRaises(ValueError):g.handle('invoice',['1000','abc','test',60])
    def test_creation_namespaced(self):
        with patch.object(g,'rpc',side_effect=[self.info(),{'label':g.PREFIX+'abc','bolt11':'lnbc'}]) as rpc:
            self.assertEqual(g.handle('invoice',['1000','abc','test',60])['label'],'abc')
            self.assertEqual(rpc.call_args.args[1][1],g.PREFIX+'abc')
            self.assertIs(rpc.call_args.args[1][6],True)
    def test_private_hints_enabled_even_when_client_default_is_false(self):
        with patch.object(g,'rpc',side_effect=[self.info(),{'label':g.PREFIX+'abc'}]) as rpc:
            g.handle('invoice',['1000','abc','test',60,None,None,False])
            self.assertIs(rpc.call_args.args[1][6],True)
    def test_no_onchain_fallback(self):
        with patch.object(g,'rpc',return_value=self.info()):
            with self.assertRaises(ValueError):g.handle('invoice',['1000','abc','test',60,['bc1fake']])
    def test_listener_skips_other_apps_payments(self):
        with patch.object(g,'rpc',side_effect=[{'label':'other-app','pay_index':7},
                {'label':g.PREFIX+'abc','pay_index':8,'status':'paid'}]) as rpc:
            result=g.handle('waitanyinvoice',[6])
            self.assertEqual(result['label'],'abc')
            self.assertEqual(result['status'],'paid')
            self.assertEqual(rpc.call_args.args[1],[7,60])

if __name__=='__main__':unittest.main()
