#!/usr/bin/env python3
"""Independent distribution/convolution audit. Reads content; never changes game prices or odds."""
from __future__ import annotations
from decimal import Decimal, getcontext
from pathlib import Path
import json, math
getcontext().prec=50
ROOT=Path(__file__).resolve().parents[2]
D=Decimal

def ssr_pmf(c:dict,start:int=0)->list[Decimal]:
    out=[D(0)]*(c['hard']-start+1);surv=D(1)
    for step in range(1,len(out)):
        k=start+step
        rate=10000 if k>=c['hard'] else min(10000,c['ssrBase']+c['softStep']*max(0,k-c['softStart']+1))
        p=D(rate)/10000;out[step]=surv*p;surv*=1-p
    assert abs(sum(out)-1)<D('1e-45')
    return out

def conv(a,b):
    out=[D(0)]*(len(a)+len(b)-1)
    for i,p in enumerate(a):
        for j,q in enumerate(b):out[i+j]+=p*q
    return out

def featured_pmf(c):
    a=ssr_pmf(c);b=conv(a,a);q=D(c['featuredSsrBps'])/10000
    return [(q*a[i] if i<len(a) else D(0))+(1-q)*b[i] for i in range(len(b))]

def cdf(p,n):return sum(p[:min(n+1,len(p))])

def reachable_loss_counts(steps=12):
    """Explore both SSR outcomes, resetting loss count on any featured SSR."""
    states={(False,0)}
    for _ in range(steps):
        nxt=set()
        for guaranteed,losses in states:
            if guaranteed:
                nxt.add((False,0))
            else:
                nxt.add((False,0))
                nxt.add((True,losses+1))
        states |= nxt
    return sorted({losses for _,losses in states})

def run():
    data=json.loads((ROOT/'data/game_data.json').read_text());c=data['gacha']['curves']['character']
    p=featured_pmf(c);g=data['gacha']['curves']['weapon'];w=featured_pmf(g)
    rows=[]
    for odds in [5000,5500]:
        cp={**c,'featuredSsrBps':odds};pm=featured_pmf(cp)
        for n in [41,46,50,81,96,110,115,120,160]:rows.append({'featured_ratio':odds/10000,'pulls':n,'probability':float(cdf(pm,n))})
    # Timing assumption: existing v0.7 weekly-spread model gives 6,560 crystals by D21.
    # 5 of the nine limited tickets by D21 (3 first-event tickets, 2 pass tickets) is an explicit scenario, not an authored schedule.
    A_budget=6560//160+5;budget=96
    pa=cdf(p,A_budget);both=sum(p[k]*cdf(p,budget-k) for k in range(1,A_budget+1));any_one=cdf(p,budget)
    pb=both+any_one-pa
    means={'ssr_character':float(sum(D(i)*v for i,v in enumerate(ssr_pmf(c)))),'featured_character':float(sum(D(i)*v for i,v in enumerate(p))),'featured_weapon':float(sum(D(i)*v for i,v in enumerate(w)))}
    # Actual shop amounts, no first-purchase doubling.
    packs=[(160,2400),(800,11000),(1680,22000),(3440,44000),(7000,88000)]
    costs={}
    for target in [12800,25600]:
        cap=target+7000;dp=[math.inf]*(cap+1);dp[0]=0;parents={}
        for v in range(cap+1):
            if dp[v]==math.inf:continue
            for amt,price in packs:
                nv=v+amt
                if nv<=cap and dp[v]+price<dp[nv]:dp[nv]=dp[v]+price;parents[nv]=(v,amt,price)
        best=min(range(target,cap+1),key=lambda t:(dp[t],t));v=best;items=[]
        while v:prev,amt,price=parents[v];items.append([amt,price]);v=prev
        costs[str(target)]={'cost_krw':dp[best],'received_crystals':best,'packs':items}
    prog=data['progression']; unlock_core=sum(a['cores'] for a in prog['levelling']['charAscension']);skill=sum(s['cores'] for s in prog['levelling']['skillCosts'] if s['level']>0) if 'level' in prog['levelling']['skillCosts'][0] else sum(s['cores'] for s in prog['levelling']['skillCosts'])
    result={'method':'50-digit Decimal; first-SSR PMF and convolution; not Monte Carlo','means':means,'cdf_scenarios':rows,
      'banner_timing_scenario':{'assumptions':['zero initial currency, zero pity, no guarantee','all 96 recurring pulls, no weapon spending','D21 crystals 6560 per inherited accounting model; assume 5 tickets by D21','stop pulling each target at first copy; saved pulls carried into second banner','A only D1-D21; B D22-D42; first-clear/launch/shop-loop rewards excluded'],
      'first_banner_budget':A_budget,'season_budget':budget,'P_first_banner_A':float(pa),'P_B':float(pb),'P_both':float(both),'P_at_least_one':float(any_one),'P_neither':float(1-any_one)},
      'pack_min_cost_no_first_bonus':costs,
      'progression_cores':{'ascend_to_60_per_character':unlock_core,'skill_10_10_per_character':skill*2,'full_per_character':unlock_core+skill*2,'six_lv60_skill8_characters':unlock_core*6,'six_full_characters':(unlock_core+skill*2)*6,'season_best_case_cores':40},
      'review_compensation_rule':{'max_losses_since_last_featured_with_guarantee_reset':max(reachable_loss_counts()),'proposed_two_losses_trigger_reachable':any(n>=2 for n in reachable_loss_counts())}}
    folder=ROOT/'reports/review_v071';folder.mkdir(parents=True,exist_ok=True);(folder/'economy_independent.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
    print(json.dumps(result,ensure_ascii=False,indent=2))
    return result
if __name__=='__main__':run()
