/* Test-only candidate data. Original live content/data and story authored flags stay unchanged.
 * Profiles: design v0.8.1 §10.4. Encounter groups: preserved external review §6.1 proposal.
 * No historical balance claim is imported as a current result.
 */
'use strict';
const enemies = [
  { id: 'e_debtwraith', name: '채무 망령', zone: 3, tier: 'normal', hp: 38, weak: ['frost', 'void'], brk: 4,
    pattern: [{ t: 'atk', d: 12 }, { t: 'apDrain', n: 1 }, { t: 'atk', d: 16 }] },
  { id: 'e_scaleknight', name: '저울 기사', zone: 3, tier: 'normal', hp: 52, weak: ['volt', 'flame'], brk: 5,
    pattern: [{ t: 'def', b: 12 }, { t: 'atk', d: 22 }, { t: 'atk', d: 10, h: 2 }] },
  { id: 'e_sealgolem', name: '인장 골렘', zone: 3, tier: 'normal', hp: 46, weak: ['flame', 'frost'], brk: 4,
    pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 24 }] },
  { id: 'e_ratpack', name: '이자 쥐떼', zone: 3, tier: 'normal', hp: 18, weak: ['flame'], brk: 2,
    pattern: [{ t: 'atk', d: 6, h: 2 }, { t: 'buff', str: 1 }] },
  { id: 'e_overdue', name: '연체 서류', zone: 3, tier: 'normal', hp: 35, weak: ['volt', 'void'], brk: 3,
    pattern: [{ t: 'insert', card: 'penalty_note', n: 1 }, { t: 'atk', d: 18 }] },
  { id: 'e_collector', name: '추심인', zone: 3, tier: 'elite', hp: 110, weak: ['frost', 'volt'], brk: 8,
    pattern: [{ t: 'apDrain', n: 2 }, { t: 'charge', label: '추심 집행 준비' }, { t: 'atk', d: 52, label: '추심 집행' }, { t: 'def', b: 15 }] },
  { id: 'e_notarytwin', name: '공증 거울 쌍', zone: 3, tier: 'elite', hp: 55, weak: ['void', 'flame'], brk: 4,
    pattern: [{ t: 'reflect', n: 4, label: '쌍둥이 거울' }, { t: 'atk', d: 14, h: 2 }, { t: 'heal', n: 8 }] },
  { id: 'b_usurer', name: '이자를 먹는 자', zone: 3, tier: 'boss', hp: 260, weak: ['void', 'frost'], brk: 13,
    pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 22 }, { t: 'apDrain', n: 2 }, { t: 'charge', label: '파산 선고 준비' }, { t: 'atk', d: 58, label: '파산 선고' }] },
  { id: 'e_choir', name: '침묵 합창단원', zone: 4, tier: 'normal', hp: 40, weak: ['volt', 'flame'], brk: 4,
    pattern: [{ t: 'silence', n: 1 }, { t: 'atk', d: 18 }] },
  { id: 'e_stagerg', name: '무대 장치', zone: 4, tier: 'normal', hp: 58, weak: ['frost'], brk: 5,
    pattern: [{ t: 'def', b: 15 }, { t: 'atk', d: 26 }] },
  { id: 'e_shadowlamp', name: '그림자 조명', zone: 4, tier: 'normal', hp: 42, weak: ['void', 'volt'], brk: 4,
    pattern: [{ t: 'debuff', s: 'weak', n: 1 }, { t: 'atk', d: 12, h: 2 }] },
  { id: 'e_ghostguest', name: '객석 유령', zone: 4, tier: 'normal', hp: 22, weak: ['flame', 'void'], brk: 2,
    pattern: [{ t: 'atk', d: 9, h: 2 }] },
  { id: 'e_prompter', name: '프롬프터', zone: 4, tier: 'normal', hp: 45, weak: ['frost', 'flame'], brk: 4,
    pattern: [{ t: 'delayRes' }, { t: 'atk', d: 20 }] },
  { id: 'e_baton', name: '지휘봉 그림자', zone: 4, tier: 'elite', hp: 125, weak: ['flame', 'volt'], brk: 9,
    pattern: [{ t: 'silence', n: 1 }, { t: 'atk', d: 16, h: 3 }, { t: 'charge', label: '종막 지휘' }, { t: 'atk', d: 56, label: '포르티시모' }] },
  { id: 'e_primadonna', name: '프리마돈나의 잔상', zone: 4, tier: 'elite', hp: 120, weak: ['frost', 'void'], brk: 9,
    pattern: [{ t: 'reflect', n: 5, label: '유리 아리아' }, { t: 'atk', d: 28 }, { t: 'heal', n: 15 }] },
  { id: 'b_overture', name: '끝나지 않는 서곡', zone: 4, tier: 'boss', hp: 290, weak: ['volt', 'frost'], brk: 14,
    pattern: [{ t: 'silence', n: 1 }, { t: 'atk', d: 24 }, { t: 'delayRes' }, { t: 'charge', label: '무음의 절정' }, { t: 'atk', d: 62, label: '종언의 화음' }] },
  { id: 'e_springguard', name: '태엽 파수꾼', zone: 5, tier: 'normal', hp: 55, weak: ['void', 'flame'], brk: 5,
    pattern: [{ t: 'def', b: 18 }, { t: 'atk', d: 28 }] },
  { id: 'e_pendulum', name: '진자 칼날', zone: 5, tier: 'normal', hp: 48, weak: ['frost'], brk: 4,
    pattern: [{ t: 'atk', d: 14, h: 2 }, { t: 'atk', d: 24 }] },
  { id: 'e_rustgear', name: '녹슨 톱니 떼', zone: 5, tier: 'normal', hp: 20, weak: ['volt'], brk: 2,
    pattern: [{ t: 'atk', d: 10, h: 2 }] },
  { id: 'e_timeshard', name: '시간 조각', zone: 5, tier: 'normal', hp: 45, weak: ['flame', 'void'], brk: 5,
    pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 26 }] },
  { id: 'e_echo', name: '이름 없는 메아리', zone: 5, tier: 'normal', hp: 50, weak: ['volt', 'frost'], brk: 4,
    pattern: [{ t: 'eatName' }, { t: 'atk', d: 22 }] },
  { id: 'e_archbell', name: '종지기 대장', zone: 5, tier: 'elite', hp: 140, weak: ['frost', 'flame'], brk: 10,
    pattern: [{ t: 'charge', label: '자정의 타종 준비' }, { t: 'atk', d: 60, label: '자정의 타종' }, { t: 'sealSlot' }, { t: 'atk', d: 18, h: 2 }] },
  { id: 'b_nameless', name: '무명', zone: 5, tier: 'boss', hp: 170, weak: ['void', 'volt'], brk: 15,
    pattern: [{ t: 'eatName' }, { t: 'atk', d: 26 }, { t: 'apDrain', n: 2 }, { t: 'charge', label: '공백 집적' }, { t: 'atk', d: 70, label: '망각의 낙인' }],
    phase: { t: 'phase', hp: 170, brk: 15, weak: ['flame', 'frost'],
      pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 18, h: 3 }, { t: 'delayRes' }, { t: 'silence', n: 1 }, { t: 'charge', label: '자정의 침묵' }, { t: 'atk', d: 70, label: '종언의 자정' }] } },
];
const encounters = {
  3: { normal: [['e_debtwraith', 'e_overdue'], ['e_scaleknight'], ['e_sealgolem', 'e_ratpack'], ['e_ratpack', 'e_ratpack', 'e_ratpack'], ['e_debtwraith', 'e_sealgolem']],
    elite: [['e_collector'], ['e_notarytwin', 'e_notarytwin']], boss: [['b_usurer']] },
  4: { normal: [['e_choir', 'e_shadowlamp'], ['e_stagerg', 'e_ghostguest'], ['e_prompter', 'e_choir'], ['e_ghostguest', 'e_ghostguest', 'e_ghostguest'], ['e_stagerg', 'e_prompter']],
    elite: [['e_baton'], ['e_primadonna']], boss: [['b_overture']] },
  5: { normal: [['e_springguard', 'e_rustgear'], ['e_pendulum', 'e_timeshard'], ['e_echo', 'e_timeshard'], ['e_rustgear', 'e_rustgear', 'e_rustgear'], ['e_springguard', 'e_echo']],
    elite: [['e_archbell']], boss: [['b_nameless']] },
};
const provenance = { status: 'candidate-only', profiles: 'docs/design_v0.8.1.md §10.4', encounterGroups: 'review/sources/external_review_v0.7.md §6.1 (unverified proposal)',
  notes: 'No narrative/client readiness or balance approval. Existing lowest-HP heal semantics retained; phase2 descriptor normalized to phase without changing numbers.' };
function withLateEnemies(data) {
  const out = JSON.parse(JSON.stringify(data));
  const ids = new Set(out.enemies.map(e => e.id));
  if (enemies.some(e => ids.has(e.id)) || Object.keys(encounters).some(zone => out.encounters[zone])) throw new Error('Late candidate data would overwrite existing content');
  out.enemies.push(...JSON.parse(JSON.stringify(enemies)));
  Object.assign(out.encounters, JSON.parse(JSON.stringify(encounters)));
  return out;
}
module.exports = { enemies, encounters, withLateEnemies, provenance };
