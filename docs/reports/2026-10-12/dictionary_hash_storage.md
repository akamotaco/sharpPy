# dict 해시와 재진입 (97차, 작업 4)

CLR 32비트 comparer 저장소를 native 폭 해시·키·값을 보존하는 저장소로 교체했다. CPython v3.12.15 dictobject.c의 perturb 탐색, 같은 해시일 때만 동등 비교, callback 후 테이블/키 검증과 재시작, 복사·merge의 저장 해시 재사용을 따른다. int/float의 동등 수치 해시, tuple 수치 해시, 사용자 hash 반환 검증도 연결했다. 기본 키에는 별도 identity 사전을 만들지 않고, callback을 실행할 수 있는 키에만 저장 해시를 위한 identity 인덱스를 둔다. 내부 반복·repr·popitem은 hash/eq를 재실행하지 않는다.

기존 삽입 순서·커서 구조는 유지한다. 작은 인덱스는 ushort, 큰 인덱스는 int 배열이며, 복사본의 엔트리 배열은 실제 사용 구간만 복사한다. CLR 딕셔너리의 comparer에서 Python callback이 저장소를 변경하는 위험도 제거했다. 별도 DLL/native 라이브러리는 추가하지 않았다.

회귀: 기본 169개 통과(출력 164, 변동 필드 3, native 2). 새 회귀는 callback 기록, 전체 해시의 상위 비트, 작은/큰 수치 키, float 특수값·subnormal, tuple 키, 갱신·setdefault·삭제·pop·복사·생성·merge·실제 view 반복·동등 비교, callback 중 clear/삭제/확장/값 교체와 재조회다. Godot 소스 직접 포함 빌드 및 기본 17개·관련 25개 통과.

성능: 96차와 1만 회, warmup 10, 표본 10, 교대 3쌍, tiering 비활성. 일반 dictionary의 프로세스 중앙값 약 7.89→7.96ms로 범위가 겹친다. iteration 약 7.84→7.73ms도 범위가 겹친다. copy는 약 15.96→12.08ms. 1만 회 할당은 각각 3,126,320→2,774,864B, 2,726,312→2,374,856B, 42,898,336→42,097,680B다. 최초 copy 구현의 할당 증가를 발견해 엔트리 복사 범위와 인덱스 폭을 줄인 뒤 재측정한 수치다.

`round97-validation.json`에 최종 전체/Godot 결과와 성능 표본·DLL 해시를 보존했다. 마지막 empty-pop/merge-capacity 보정은 전체 기능 검증에 포함되며 성능 표본은 그 직전 저장소 빌드다. 전체 hashable 타입이나 dict의 모든 미구현 API를 완성했다는 뜻은 아니다. set의 별도 저장소는 이번 범위가 아니다. 작업 5~6은 미완료다.
