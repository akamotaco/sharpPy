# Versioned Globals Dict 설계 (LOAD_GLOBAL 사이트 캐시 전제 작업)

작성: 2026-06-12 (v0.4.9). 분석 근거: docs/gap_analysis_top10_v0.4.9.md, bench_super_ladder.py

## 목표

`LOAD_GLOBAL` builtin 경로의 호출당 ~125ns (globals dict 미스 + builtins dict 조회)를
CPython 3.12 `LOAD_GLOBAL_BUILTIN` 수준 (~15ns, 버전 가드 + 캐시 슬롯 직행)으로 단축.
super()/builtin 호출이 잦은 모든 코드의 공통 비용이며, inheritance 벤치 잔여 갭의 핵심.

## CPython 3.12 메커니즘 (Python/specialize.c, bytecodes.c)

```
LOAD_GLOBAL_BUILTIN:
    DEOPT_IF(GLOBALS()->ma_keys->dk_version != read_u16(&cache->module_keys_version))
    DEOPT_IF(BUILTINS()->ma_keys->dk_version != read_u16(&cache->builtin_keys_version))
    res = entries[cache->index].me_value   // dict 엔트리 직접 인덱싱
```
- dict 의 **keys 버전** (dk_version): 키 집합이 바뀔 때만 증가 (값 갱신은 무관!)
  → globals 에 같은 이름이 새로 추가되어 builtin 을 가리는 경우만 무효화하면 됨
- 값 갱신 무효화는 LOAD_GLOBAL_MODULE 의 index 직행으로 자연 해결 (값은 매번 읽음)

## SharpPy 현황과 제약

- globals = `Dictionary<string, PyObject>` (PyFrame.Globals / PyScope.Variables / PyModule.ModuleDict 가 동일 인스턴스 공유)
- **C# 코드가 dict 를 직접 변형하는 지점이 다수** (import 기계, 모듈 초기화, exec 등)
  → PyScope 레벨 카운터로는 누수 (직접 쓰기가 우회) — 반드시 dict 타입 자체가 버전을 소유해야 함
- `Dictionary<TKey,TValue>` 는 메서드가 non-virtual → 상속 래퍼로는 base 참조 경유 쓰기를 잡을 수 없음

## 설계: PyGlobalsDict (composition)

```csharp
// CPython PyDictKeysObject.dk_version 대응. 키 집합 변경시에만 KeysVersion 증가.
public sealed class PyGlobalsDict
{
    private readonly Dictionary<string, PyObject> _d = new();
    public ulong KeysVersion { get; private set; } = 1;

    public bool TryGetValue(string k, out PyObject v) => _d.TryGetValue(k, out v);
    public PyObject this[string k]
    {
        get => _d[k];
        set { if (!_d.ContainsKey(k)) KeysVersion++; _d[k] = value; }  // 키 추가시에만 bump
    }
    public bool Remove(string k) { if (_d.Remove(k)) { KeysVersion++; return true; } return false; }
    // ContainsKey / Count / GetEnumerator / Keys / Values 위임
}
```

### 교체 범위 (전면 타입 교체 — 리팩토링 규모 큼)
1. `PyModule.ModuleDict`, `PyScope.Variables` (Global 타입 스코프), `PyFrame.Globals`
2. `PyFunction.GlobalsDict`, `globals()` builtin 반환 경로 (PyDict 변환 시 래퍼 유지 필요)
3. exec/eval 의 globals 파라미터 경로
4. 직접 변형 지점 전수 조사: `grep "ModuleDict\[" / "Globals\[" / ".Variables\["`

### LOAD_GLOBAL 사이트 캐시 (PyCodeObject side-table)
```csharp
// 명령어 인덱스 → 캐시 (CACHE 슬롯 4개에 대응하는 side-table)
struct GlobalSiteCache { ulong GlobalsKeysVersion; ulong BuiltinsKeysVersion; PyObject Value; }
GlobalSiteCache[] _globalSiteCache;  // LOAD_GLOBAL 사이트 수만큼, lazy
```
inline LOAD_GLOBAL 핸들러:
1. 캐시 히트 (양쪽 KeysVersion 일치) → Value push (검사 2회 + push)
2. 미스 → 기존 2-조회 경로 + 캐시 갱신

### 검증 시나리오 (CPython 동작 동일성)
```python
import builtins
def f(): return len  # builtin
f()                       # 캐시 형성
len = lambda x: 42        # 모듈 globals 에 len 추가 (keys version bump)
f()                       # → 새 len 반환되어야 함 (shadowing)
del len                   # keys version bump
f()                       # → builtin len 복귀
builtins.len = ...        # builtins keys 는 불변, 값 갱신 → 캐시는 값 자체를 들고 있으므로
                          # 값 갱신 감지 못함 ⚠ → builtins 캐시는 BuiltinsKeysVersion 외에
                          # 값 동일성도... CPython 도 dk_version 만 검사 (값 갱신은 entries
                          # 인덱스 직행으로 해결) → SharpPy 도 Value 대신 "조회 결과의 출처
                          # (globals/builtins) + 키" 를 캐시하고 값은 해당 dict 에서 1회 조회로
                          # 읽는 변형이 안전 (그래도 2-조회 → 1-조회 + 검사 2회로 단축)
```

### 단계
1. PyGlobalsDict 타입 + 전면 교체 (기능 변화 없음, 회귀 전체 통과 확인) — 1 커밋
2. LOAD_GLOBAL 사이트 캐시 + 검증 시나리오 테스트 — 1 커밋
3. 측정: bench_super_ladder L2 (super 이름 로드) 130ns → 목표 30ns 이하

### 리스크
- globals() 가 반환하는 객체의 정체성/변형 반영 (CPython 은 실제 모듈 dict 반환 — 래퍼가
  PyDict 와 호환되어야 함; SharpPy 의 globals() 구현 확인 필요)
- Godot/AOT: 순수 C# — 영향 없음
