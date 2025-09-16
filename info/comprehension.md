# CPython 3.12 Comprehension 구현 분석

## 개요
CPython 3.12에서 도입된 PEP 709 (Inlined Comprehensions)에 의해 comprehension이 별도 함수 없이 인라인으로 처리되며, 새로운 `LOAD_FAST_AND_CLEAR` opcode가 변수 격리를 담당합니다.

## Dict Comprehension 분석

### CPython 3.12 소스코드 구현

#### LOAD_FAST_AND_CLEAR Opcode
```c
inst(LOAD_FAST_AND_CLEAR, (-- value)) {
    value = GETLOCAL(oparg);
    // do not use SETLOCAL here, it decrefs the old value
    GETLOCAL(oparg) = NULL;
}
```

**동작 원리**:
1. `GETLOCAL(oparg)`: 로컬 변수 슬롯에서 값 읽기
2. 값이 없으면 `NULL` 반환
3. 로컬 변수 슬롯을 `NULL`로 클리어
4. 기존 값을 스택에 푸시 (백업 목적)

#### MAP_ADD Opcode
```c
inst(MAP_ADD, (key, value --)) {
    PyObject *dict = PEEK(oparg + 2); // key, value are still on the stack
    assert(PyDict_CheckExact(dict));
    // dict[key] = value
    ERROR_IF(_PyDict_SetItem_Take2((PyDictObject *)dict, key, value) != 0, error);
    PREDICT(JUMP_BACKWARD);
}
```

**핵심 메커니즘**:
- `PEEK(oparg + 2)`: 스택에서 상대적 위치로 dict 찾기
- 스택 수정 없이 값만 조회
- `oparg` 값은 항상 `2`로 고정 (3중 중첩에서도)

### 변수 격리 메커니즘 (PEP 709)

#### 기본 원리
```python
x = "outer_x"
result = {x: x*2 for x in range(3)}
# x는 여전히 "outer_x" (격리됨)
```

#### 바이트코드 패턴
1. **백업**: `LOAD_FAST_AND_CLEAR x` → 기존 x값을 스택에 저장, x를 NULL로 클리어
2. **실행**: comprehension 내부에서 x는 iteration variable로 사용
3. **복원**: `STORE_FAST x` → 백업된 값을 x에 복원

### 3중 Dict Comprehension 바이트코드 분석

#### CPython 3.12 패턴
```python
{x: {y: {z: x+y+z for z in range(1)} for y in range(1)} for x in range(1)}
```

**Level 0 (최외곽)**:
```
LOAD_FAST_AND_CLEAR 0 (x)    # x 백업 (NULL이면 NULL)
LOAD_FAST_AND_CLEAR 1 (y)    # y 백업 (NULL이면 NULL)
LOAD_FAST_AND_CLEAR 2 (z)    # z 백업 (NULL이면 NULL)
SWAP 4                        # 스택 재배치
BUILD_MAP 0                   # 빈 dict 생성
SWAP 2                        # dict를 올바른 위치로
```

**Level 1 (중간)**:
```
LOAD_FAST_AND_CLEAR 1 (y)    # y 백업
LOAD_FAST_AND_CLEAR 2 (z)    # z 백업
SWAP 3                        # 스택 재배치
BUILD_MAP 0                   # 빈 dict 생성
SWAP 2                        # dict 위치 조정
```

**Level 2 (최내곽)**:
```
LOAD_FAST_AND_CLEAR 2 (z)    # z 백업
SWAP 2                        # 스택 재배치
BUILD_MAP 0                   # 빈 dict 생성
SWAP 2                        # dict 위치 조정
```

**모든 레벨에서 MAP_ADD 2 사용**

### 스택 상태 분석

#### 정상 케이스 (함수 내부)
```
변수 존재: x="func_x", y="func_y", z="func_z"
스택 상태: [iterator, "func_x", "func_y", "func_z"]
→ SWAP 4 → ["func_x", "func_y", "func_z", iterator]
→ BUILD_MAP → ["func_x", "func_y", "func_z", iterator, {}]
→ SWAP 2 → ["func_x", "func_y", "func_z", {}, iterator]
```

#### 문제 케이스 (모듈 레벨)
```
변수 없음: x, y, z 미정의
스택 상태: [iterator, NULL, NULL, NULL]
→ SWAP 4 → [NULL, NULL, NULL, iterator]
→ BUILD_MAP → [NULL, NULL, NULL, iterator, {}]
→ SWAP 2 → [NULL, NULL, NULL, {}, iterator]
```

### SharpPy vs CPython 차이점

| 측면 | CPython 3.12 | SharpPy (현재) |
|------|-------------|----------------|
| **NULL 처리** | NULL 스택에 푸시, MAP_ADD가 PEEK으로 dict 찾기 | NULL→PyNone 변환, MAP_ADD 실패 |
| **MAP_ADD 구현** | `PEEK(oparg + 2)` 상대 위치 | 절대 스택 위치 계산 |
| **변수 스코프** | 함수 로컬 변수 | 전역/모듈 변수 |
| **에러 처리** | NULL 값 정상 처리 | `RuntimeError: MAP_ADD: target is not a dict, got PyNone` |

### SharpPy 문제점

1. **LOAD_FAST_AND_CLEAR**: 존재하지 않는 변수에서 NULL 대신 PyNone 반환
2. **MAP_ADD**: PEEK 방식이 아닌 절대 위치로 dict 찾기 시도
3. **스택 관리**: NULL 값들이 스택 레이아웃을 오염시켜 MAP_ADD 실패

### 성능 개선 효과 (PEP 709)
- **기존 방식**: 각 comprehension마다 새 함수 객체 생성
- **새 방식**: 인라인 처리로 최대 2배 성능 향상
- **벤치마크**: `[x for x in l]` 실행 시간 108ns → 60.9ns

## List Comprehension 분석

### CPython 3.12 LIST_APPEND Opcode
```c
inst(LIST_APPEND, (list, unused[oparg-1], v -- list, unused[oparg-1])) {
    ERROR_IF(_PyList_AppendTakeRef((PyListObject *)list, v) < 0, error);
    PREDICT(JUMP_BACKWARD);
}
```

**동작 원리**:
- `v` 값을 `list`에 추가
- `_PyList_AppendTakeRef()`: 값의 소유권을 가져감
- 리스트와 다른 스택 요소들은 보존
- JUMP_BACKWARD 예측 (루프 최적화)

### 바이트코드 패턴 분석

#### 1중 List Comprehension
```python
[x*2 for x in range(3)]
```

**CPython 3.12 패턴**:
```
LOAD_FAST_AND_CLEAR 0 (x)    # x 백업
SWAP 2                        # 스택 재배치
BUILD_LIST 0                  # 빈 리스트 생성
SWAP 2                        # 리스트를 올바른 위치로
FOR_ITER ...                  # 루프 시작
  STORE_FAST 0 (x)           # iteration variable 저장
  LOAD_FAST 0 (x)            # x 로드
  LOAD_CONST 2               # 2 로드
  BINARY_OP * (5)            # x * 2 계산
  LIST_APPEND 2              # 결과를 리스트에 추가
  JUMP_BACKWARD ...          # 루프 계속
END_FOR                       # 루프 종료
SWAP 2                        # 백업된 값 복원 위치
STORE_FAST 0 (x)             # 원래 x 값 복원
```

#### 2중 List Comprehension
```python
[x+y for x in range(2) for y in range(2)]
```

**CPython 3.12 패턴**:
```
LOAD_FAST_AND_CLEAR 0 (x)    # x 백업
LOAD_FAST_AND_CLEAR 1 (y)    # y 백업
SWAP 3                        # 스택 재배치
BUILD_LIST 0                  # 빈 리스트 생성
SWAP 2                        # 리스트 위치 조정
# 외부 루프
FOR_ITER ...
  STORE_FAST 0 (x)
  # 내부 루프
  FOR_ITER ...
    STORE_FAST 1 (y)
    LOAD_FAST 0 (x)
    LOAD_FAST 1 (y)
    BINARY_OP + (0)
    LIST_APPEND 3             # 3단계 아래 리스트에 추가
    JUMP_BACKWARD ...
  END_FOR
  JUMP_BACKWARD ...
END_FOR
SWAP 3                        # 백업된 값들 복원
STORE_FAST 1 (y)
STORE_FAST 0 (x)
```

#### 3중 List Comprehension
```python
[x+y+z for x in range(1) for y in range(1) for z in range(1)]
```

**CPython 3.12 패턴**:
```
LOAD_FAST_AND_CLEAR 0 (x)    # x 백업
LOAD_FAST_AND_CLEAR 1 (y)    # y 백업
LOAD_FAST_AND_CLEAR 2 (z)    # z 백업
SWAP 4                        # 스택 재배치
BUILD_LIST 0                  # 빈 리스트 생성
SWAP 2                        # 리스트 위치 조정
# 3중 중첩 루프...
LIST_APPEND 4                 # 4단계 아래 리스트에 추가
```

### LIST_APPEND vs MAP_ADD 차이점

| 측면 | LIST_APPEND | MAP_ADD |
|------|-------------|---------|
| **스택 구조** | `(list, unused[oparg-1], v -- list, unused[oparg-1])` | `(key, value --)` |
| **타겟 찾기** | 직접 스택에서 list 파라미터 | `PEEK(oparg + 2)`로 dict 찾기 |
| **oparg 값** | 중첩 레벨별로 증가 (2,3,4) | 모든 레벨에서 2로 고정 |
| **값 처리** | `_PyList_AppendTakeRef(list, v)` | `_PyDict_SetItem_Take2(dict, key, value)` |

### 중첩 List Comprehension (Nested)
```python
[[x+y for y in range(2)] for x in range(2)]
```

**CPython 3.12 패턴**:
- 외부: `LIST_APPEND 2` (외부 리스트에 내부 리스트 추가)
- 내부: `LIST_APPEND 2` (내부 리스트에 값 추가)
- 각각 별도의 BUILD_LIST와 변수 백업/복원

### SharpPy LIST_APPEND 문제점

**오류**: `RuntimeError: LIST_APPEND: target is not a list, got iterator`

**원인 분석**:
1. **스택 위치 계산 오류**: NULL 값들이 스택을 오염시켜 LIST_APPEND가 잘못된 위치에서 타겟을 찾음
2. **oparg 해석 차이**: CPython의 `LIST_APPEND(oparg)`에서 oparg는 스택 아래로 내려갈 단계 수
3. **LOAD_FAST_AND_CLEAR NULL 처리**: 존재하지 않는 변수에서 NULL 반환 시 스택 레이아웃 파괴

**스택 상태 비교**:
```
CPython 3.12 (정상):
[backup_x, backup_y, list, iterator] → LIST_APPEND 3 → list 찾기 성공

SharpPy (문제):
[NULL, NULL, list, iterator] → LIST_APPEND 3 → iterator를 list로 오인
```

---

## 구현 참고사항

### CPython 3.12 호환 구현을 위한 핵심 포인트
1. **MAP_ADD에 PEEK 방식 구현**: `PEEK(oparg + 2)`로 dict 찾기
2. **NULL 값 안전 처리**: PyNone이 아닌 진짜 NULL 처리
3. **변수 격리 메커니즘**: 백업/복원 패턴 정확히 구현
4. **Exception Table**: 백업된 변수들의 적절한 복원 보장

### SharpPy 최적화 구현 대안
1. **LOAD_FAST_AND_CLEAR 패턴 제거**: 단순한 BUILD_MAP/BUILD_LIST 방식
2. **MAP_ADD/LIST_APPEND 위치 계산 단순화**: 항상 2로 고정
3. **Exception Handler 간소화**: 복잡한 백업/복원 제거

## 공통 해결 전략

### 근본 원인
**List와 Dict Comprehension 모두 동일한 문제**: LOAD_FAST_AND_CLEAR가 NULL을 반환할 때 스택 레이아웃이 파괴되어 LIST_APPEND/MAP_ADD가 잘못된 타겟을 참조

### 해결 방안 비교

| 방안 | 장점 | 단점 |
|------|------|------|
| **1. CPython 호환** | 바이트코드 동일, PEP 709 완전 구현 | 복잡성, NULL 처리 어려움 |
| **2. SharpPy 최적화** | 단순성, 안정성, 유지보수성 | 바이트코드 차이 |
| **3. 하이브리드** | 점진적 개선 가능 | 개발 시간 증가 |

### 권장 구현 순서
1. **즉시 해결**: LOAD_FAST_AND_CLEAR 패턴 제거로 안정성 확보
2. **중기 목표**: MAP_ADD/LIST_APPEND PEEK 방식 구현
3. **장기 목표**: 완전한 PEP 709 호환성 달성

### 핵심 수정 포인트
- **PyCompiler.cs**: `CompileListComprehension`, `CompileDictComprehension` 메서드
- **VM.cs**: `LIST_APPEND`, `MAP_ADD` opcode 구현
- **Exception handling**: 백업/복원 메커니즘

---

## 참고 자료
- **PEP 709**: Inlined comprehensions
- **CPython 3.12**: `Python/bytecodes.c`
- **성능 벤치마크**: List comprehension 2x 향상, Dict comprehension 유사
- **호환성**: Python 3.12+ 표준 준수 필요

---

*마지막 업데이트: CPython 3.12 list/dict comprehension 완전 분석 완료*
*작성일: 2025년 9월 16일*
*분석 범위: 1중~4중 중첩, LOAD_FAST_AND_CLEAR, LIST_APPEND/MAP_ADD 메커니즘*