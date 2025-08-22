def check_number_for_exception(number):
    """
    음수이면 ValueError를 발생시키는 함수
    """
    if number < 0:
        # 음수를 입력받으면 ValueError 예외 발생
        raise ValueError("음수는 입력할 수 없습니다.")
    else:
        print(f"{number}는 유효한 숫자입니다.")

# 예외를 발생시키고 처리하는 예제
try:
    num1 = 10
    check_number_for_exception(num1) # 양수를 입력하면 정상 실행

    num2 = -5
    check_number_for_exception(num2) # 음수를 입력하면 ValueError 발생
except ValueError as e:
    # 발생한 ValueError를 except 블록에서 처리
    print(f"에러 발생: {e}")

print("프로그램이 계속 실행됩니다.")