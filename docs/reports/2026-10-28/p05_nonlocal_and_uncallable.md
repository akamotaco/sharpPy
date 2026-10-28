# p05 종료: nonlocal 컴파일 시점 검증 + check_caller SyntaxWarning

## 1) nonlocal 검증 (CPython Python/symtable.c analyze_block)
규칙 (18케이스 실측 매트릭스로 확정):
- 모듈 스코프의 `nonlocal` → `nonlocal declaration not allowed at module level`
  (bound=NULL은 모듈뿐이다).
- 클래스 본문의 바인딩은 메서드에서 nonlocal 대상으로 **보이지 않는다**
  (class 블록은 children bound에 자기 locals를 넣지 않음, `__class__`/
  `__classdict__`만). 단 클래스 body 안에서는 bound가 non-NULL이라
  `no binding ... found`가 난다.
- 모듈 레벨 이름은 결코 nonlocal을 만족시키지 못한다 (module children은
  empty-but-non-NULL bound).
- 함수 locals(할당·매개변수·import 머리이름)만 bound에 누적,
  `global` 선언 이름은 bound에서 제외.
- 같은 이름 global+nonlocal → `name 'x' is nonlocal and global`.
- 에러 위치는 해당 이름의 첫 directive 줄.
구현: `runtime/PySymbolTable.cs`에 directive 줄 기록 +
`ValidateNonlocalBindings()` 검증 패스, `compile.cs` 모듈 빌드 직후 호출.
부수 수정: symtable의 `import a.b` 바인딩 이름을 `a.b`→`a`( 머리부 )로 정정
(`import a.b as c`는 c). 없으면 `nonlocal os` 오탐.

## 2) `(1)()` 계열 컴파일 경고 (Python/compile.c check_caller/infer_type)
- Call 함수 자리가 Constant/tuple/list/listcomp/dict/dictcomp/set/setcomp/
  genexpr/f-string 이면 컴파일 시점에
  `File:L: SyntaxWarning: 'T' object is not callable; perhaps you missed a comma?`
  + 소스 에코 라인을 stderr로 출력 (cp와 동일 서식, 문자열 compile이면
  `<s>:L:` + 에코 없음).
- 런타임 TypeError와 별개. 매트릭스 stdout 13케이스 바이트 동일.

## 3) 덤으로 잡은 실제 버그 2개
- **PyIterator.Call이 Next()로 전달**되고 있었다: `genexp()`가 항목을 반환하고
  소진 시 StopIteration이 유출됐다. CPython에서 iterator는 호출 불가
  (`'generator' object is not callable`). `type/PyIterator.cs`를 TypeError
  경로로 교체. `g(5)`의 `__next__() takes no arguments` 오메시지도 사라짐.
- **PyASTOptimizer가 Call/BinOp/Unary/Tuple/List 노드 재구성 시 소스 위치를
  유실** (`(1)()` 경고 줄이 0으로 나오던 원인). 복원 시 LineNo 복사용 복사
  헬퍼 추가. 부수 효과로 traceback의 줄 표시가 일부 살았다.

## 검증
- `tests/bughunt/p05_call_semantics.py`: 완전 일치 (잔여 4줄 → 0).
- 신규 매트릭스 `matrix_nonlocal_errors.py`(21케이스),
  `matrix_uncallable_warnings.py`(13케이스) 모두 통과.
- evaluate: 225+3+2 변화 없음. bughunt 총량 `28 → 24`
  (p07 10, p08 14만 잔여). bughunt 스윗 16개.
- perf probe: list_comp +68% 스파이크는 재측정 -2.4%로 노이즈 판정,
  전 workload 노이즈 범위.
