// mulberry32 기반 시드 난수. 상태가 숫자 하나(BattleState.rng)라서
// 스냅샷을 복원하면 난수 흐름까지 그대로 되돌아간다(되돌리기 후 재사용 시 같은 결과).

export function rngNext(state: { rng: number }): number {
  state.rng = (state.rng + 0x6d2b79f5) | 0;
  let t = state.rng;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t = (t + Math.imul(t ^ (t >>> 7), t | 61)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

export function rngInt(state: { rng: number }, maxExclusive: number): number {
  return Math.floor(rngNext(state) * maxExclusive);
}

export function shuffleInPlace<T>(state: { rng: number }, items: T[]): void {
  for (let i = items.length - 1; i > 0; i--) {
    const j = rngInt(state, i + 1);
    [items[i], items[j]] = [items[j], items[i]];
  }
}
