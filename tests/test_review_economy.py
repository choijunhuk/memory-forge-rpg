import importlib.util,json,unittest
from decimal import Decimal
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('audit',ROOT/'tools/review/economy_audit.py')
A=importlib.util.module_from_spec(spec);spec.loader.exec_module(A)
DATA=json.loads((ROOT/'data/game_data.json').read_text())
C=DATA['gacha']['curves']['character']
def markov_hit(c,n):
    states={(0,False):1.0};hit=0.0
    for _ in range(n):
        nxt={}
        for (k,g),mass in states.items():
            kk=k+1;rate=1.0 if kk>=c['hard'] else min(1.,(c['ssrBase']+c['softStep']*max(0,kk-c['softStart']+1))/10000)
            q=1. if g else c['featuredSsrBps']/10000
            hit+=mass*rate*q
            if rate<1:nxt[(kk,g)]=nxt.get((kk,g),0)+mass*(1-rate)
            if not g:nxt[(0,True)]=nxt.get((0,True),0)+mass*rate*(1-q)
        states=nxt
    return hit
class ReviewEconomy(unittest.TestCase):
    def test_distribution_matches_independent_state_machine(self):
        for q in [5000,5500]:
            c={**C,'featuredSsrBps':q};p=A.featured_pmf(c)
            for n in [46,80,96,110,115,160]:self.assertAlmostEqual(float(A.cdf(p,n)),markov_hit(c,n),places=12)
    def test_pmf_mass_and_hard_guarantee(self):
        p=A.featured_pmf(C);self.assertLess(abs(sum(p)-1),Decimal('1e-44'));self.assertEqual(A.cdf(p,0),0);self.assertAlmostEqual(float(A.cdf(p,160)),1.)
    def test_featured_mean_equals_ssr_times_expected_ssrs(self):
        p=A.featured_pmf(C);a=A.ssr_pmf(C)
        mu=sum(Decimal(i)*v for i,v in enumerate(p));s=sum(Decimal(i)*v for i,v in enumerate(a))
        self.assertLess(abs(mu-s*(2-Decimal(C['featuredSsrBps'])/10000)),Decimal('1e-44'))
    def test_timing_events_have_consistent_inclusion_exclusion(self):
        p=A.featured_pmf(C);pa=A.cdf(p,46);both=sum(p[k]*A.cdf(p,96-k) for k in range(1,47));anyone=A.cdf(p,96);pb=both+anyone-pa
        self.assertGreater(pa,both);self.assertGreater(pb,both);self.assertGreater(anyone,pb);self.assertLess(abs(pa+pb-both-anyone),Decimal('1e-44'))
    def test_loss_counter_since_last_featured_cannot_reach_two(self):
        self.assertEqual(A.reachable_loss_counts(100),[0,1])
    def test_more_currency_or_higher_featured_ratio_never_worsens_cdf(self):
        p=A.featured_pmf(C);q=A.featured_pmf({**C,'featuredSsrBps':5500})
        for n in range(160):
            self.assertLessEqual(A.cdf(p,n),A.cdf(p,n+1));self.assertLessEqual(A.cdf(p,n),A.cdf(q,n))
