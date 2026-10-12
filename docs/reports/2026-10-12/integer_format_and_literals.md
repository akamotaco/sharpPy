# 115차: 큰 정수 format 및 컴파일 리터럴 제한

int.__format__의 Int64 축소를 제거했다. d/n 및 binary/octal/hex의 폭, 부호,
정렬, prefix, 쉼표/underscore 그룹과 zero padding을 BigInteger 값에 적용한다.
정수 precision/z, c의 부호/#, 잘못된 grouping 등 오류 검사 순서도 비교했다.
c는 Unicode scalar 폭으로 padding하며, supplementary 문자는 UTF-16 두 코드
단위여도 표시 폭 1로 계산한다. 빈 spec은 원래 객체의 str을 사용한다.

10진 출력 제한은 그룹/폭 적용 전에 검사한다. 64-bit 범위 값은 최소 허용
제한 640자리보다 작으므로 Int64 문자열 변환으로 바로 처리한다.
padding은 최종 문자열에 직접 채워 중간 padding 문자열 할당을 제거했다.
2/8/16진 변환은 BigInteger 반복 나눗셈/앞 삽입 대신 unsigned bytes에서
각 자릿수 비트를 한 번씩 추출한다.

컴파일 시 10진 정수 토큰도 현재 sys 자릿수 제한을 확인한다. underscores를
제외한 자릿수로 계산하고 SyntaxError 메시지/위치를 보존한다. CPython parser의
native integer fast path가 긴 all-zero 리터럴을 허용하는 예외도 실제 실행으로
확인했다. hex, float, 제한 0은 허용한다. compile의 포괄 catch가 이미 생성된
Python SyntaxError를 invalid syntax로 다시 감싸지 않게 했다.

참고 formatter 원본은 CPython v3.12.15 commit
e848e4b09ca4bc67c0041c9ce7526dae471d4c36의 Python/formatter_unicode.c.
SHA256 c21a3b86ae372d81534319d02025010b2b412b178997f0c5efce3c1138b50041.
기존 수정 Lib나 전체 parser를 원본으로 교체하지 않았다.

## 검증/성능

8개 정수×18개 spec, 문자 padding/오류/제한과 eval/exec/single 컴파일
639/640/641자리, underscore, 5000자리 zero/hex, 제한 해제를 비교했다.
최종 관련 3개와 Godot 기본 17+관련 3개/경로 5개 일치.
전체 211개 실행에서 210개 통과, 신규 literal fixture가 별도 str.join(string)
미지원에 걸렸다. fixture의 소스 생성만 반복/연결로 바꾸고 관련 3개를 재실행했다.
join 실패 및 Unicode repr 실패는 별도 compatibility fixture로 보존했다.
최종 일괄 재검증은 후속 6번에서 수행한다.

- 1025-bit bin 1000회, 10 samples×3쌍: 203.34885→1.7747 ms.
  할당 104,345,672→6,609,672 B. 이 비율을 VM 전체 성능으로 해석하지 않는다.
- 작은 08d format 초기 1000회는 1.15435→1.228 ms로 느려졌다.
  native-small 변환과 직접 padding 후 10000회·15 samples×3쌍:
  10.6769→10.723 ms, 범위 10.569–11.0517 / 10.6382–11.019로 겹친다.
  할당 3,992,464→3,671,664 B. 작은 format 속도 향상은 주장하지 않는다.
- 첫 벤치마크는 workload 반환값이 하네스의 합계 검산 규칙과 달라 종료했다.
  반환값을 규칙에 맞추고 결과 길이를 확인한 뒤 위 측정을 수행했다.

범위 한계: n은 현재 locale 모듈의 확장 locale 동작 전체를 검증한 것이 아니다.
float spec는 기존 float formatter에 위임하며 그 전체 정확도까지 보장하지 않는다.
전체 SyntaxError의 가변 필드 계약 및 모든 compile 옵션도 이번 범위 밖이다.
round115-validation.json에 실패와 수정 후 결과를 함께 보존했다.
