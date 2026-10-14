// Authoring helpers for Memory Forge v0.7 content.
// Every helper returns plain JSON so data/*.json stays engine-agnostic.
// Value fields: v = immediate, rv = resolved as a NEXT-turn reservation,
// ra = resolved as an AFTER-NEXT reservation (defaults to rv when omitted).

export const D = (v, o = {}) => ({ op: 'dmg', v, ...o });          // damage
export const B = (v, o = {}) => ({ op: 'blk', v, ...o });          // party block
export const H = (v, o = {}) => ({ op: 'heal', v, ...o });         // party heal
export const ST = (s, n, o = {}) => ({ op: 'st', s, n, ...o });    // enemy status: vul / weak / mark / burn
export const BUFF = (s, n, o = {}) => ({ op: 'buff', s, n, ...o }); // party buff (charge ...)
export const CTR = (n, o = {}) => ({ op: 'counter', n, ...o });    // counter stacks
export const DRAW = (n, o = {}) => ({ op: 'draw', n, ...o });
export const DISCARD = (n) => ({ op: 'discard', n });
export const AP = (n) => ({ op: 'ap', n });
export const GAUGE = (n, who = 'self') => ({ op: 'gauge', n, who });
export const CREATE = (card, n = 1, zone = 'hand') => ({ op: 'create', card, n, zone });
export const PULL = () => ({ op: 'pull' });
export const SLOT = (next = 0, after = 0) => ({ op: 'slot', next, after });
export const GROW = () => ({ op: 'grow' });
export const GROWPICK = (n = 1) => ({ op: 'growPick', n });
export const LOAN = (n) => ({ op: 'loan', n });
export const DEFER = () => ({ op: 'defer' });
export const RESTRAINT = () => ({ op: 'restraint' });
export const COLLATERAL = (v) => ({ op: 'collateral', v });
export const OVERLOAD = (v) => ({ op: 'selfdmg', v });
export const CLEANSE = () => ({ op: 'cleanse' });
export const EXPICK = (from = 'hand', n = 1) => ({ op: 'exhaustPick', from, n });
export const EXHAND = () => ({ op: 'exhaustHand' });
export const TUNE = (part, pick = 'one') => ({ op: 'tune', part, pick });
export const COSTDOWN = (n = 1, pick = 'one') => ({ op: 'costdown', n, pick });
export const RECALL = () => ({ op: 'recall' });
export const COPYPICK = (filter) => ({ op: 'copyPick', filter });
export const POWER = (id, params = {}) => ({ op: 'power', id, ...params });
export const ECHO = (n) => ({ op: 'echo', n });
export const NEXTBOOST = (bps) => ({ op: 'nextBoost', bps });
export const FREENEXT = (n) => ({ op: 'freeNext', n });
export const CLEARDEBT = () => ({ op: 'clearDebt' });
export const IF = (cond, then, els = []) => ({ op: 'if', cond, then, else: els });

// conditions
export const C = {
  marked: { c: 'marked' }, burning: { c: 'burning' }, broken: { c: 'broken' },
  debt: { c: 'debt' }, modded: { c: 'modded' }, reserved: { c: 'reserved' },
  intentAttack: { c: 'intentAttack' },
  echo: (n) => ({ c: 'echo', n }),
  hpBelow: (pct) => ({ c: 'hpBelow', pct }),
  targetHpBelow: (pct) => ({ c: 'targetHpBelow', pct }),
  growthInHand: (n) => ({ c: 'growthInHand', n }),
  exhaustedAtLeast: (n) => ({ c: 'exhaustedAtLeast', n }),
};

// scaling: value += k * counter (capped by max)
export const per = (counter, k, max) => (max === undefined ? { per: counter, k } : { per: counter, k, max });

let registry = [];
export const resetRegistry = () => { registry = []; };
export const allCards = () => registry;

// card(id, name, owner, opts)
// opts: rar B|A|S|T|C, cost, type atk|skl, res y|n|o|after, eff [ops], kw [], uses, plus {}, evo [idA,idB], flavor
export function card(id, name, owner, o) {
  const c = {
    id, name, owner,
    rar: o.rar ?? 'B',
    cost: o.cost ?? 1,
    type: o.type ?? 'atk',
    res: o.res ?? 'y',
    eff: o.eff ?? [],
    kw: o.kw ?? [],
  };
  if (o.uses) c.uses = o.uses;
  if (o.plus) c.plus = o.plus;
  if (o.evo) c.evo = o.evo;
  if (o.growth) c.growth = true;
  if (o.attr) c.attr = o.attr;
  if (o.flavor) c.flavor = o.flavor;
  if (o.start) c.start = true;
  if (o.pool === false) c.pool = false;
  registry.push(c);
  return c;
}
