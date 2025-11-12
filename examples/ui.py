"""
Progress Quest CUI Interface
콘솔 기반 사용자 인터페이스
"""
import sys
import time
from game_logic import Character, GameState, NameGenerator


class ProgressQuestUI:
    """CUI 인터페이스 클래스"""
    
    def __init__(self):
        self.game_state = None
    
    @staticmethod
    def clear_screen():
        """화면 클리어 (간단한 방식)"""
        print("\n" * 2)
    
    @staticmethod
    def print_header(text):
        """헤더 출력"""
        print("=" * 70)
        print(f" {text}")
        print("=" * 70)
    
    @staticmethod
    def print_progress_bar(label, progress, width=30):
        """진행 바 출력"""
        filled = int(width * progress)
        bar = "█" * filled + "░" * (width - filled)
        percentage = int(progress * 100)
        print(f"{label:20s} [{bar}] {percentage:3d}%")
    
    def create_character(self):
        """캐릭터 생성"""
        self.clear_screen()
        self.print_header("PROGRESS QUEST - Character Creation")
        
        # 이름 입력
        print("\nEnter your character name (or press Enter for random): ", end="")
        name = input().strip()
        if not name:
            name = f"Hero{random.randint(1000, 9999)}"
        
        # 종족 선택
        print("\nAvailable Races:")
        for i, race in enumerate(Character.RACES, 1):
            print(f"  {i}. {race}")
        
        print("\nSelect race (or press Enter for random): ", end="")
        race_input = input().strip()
        if race_input and race_input.isdigit() and 1 <= int(race_input) <= len(Character.RACES):
            race = Character.RACES[int(race_input) - 1]
        else:
            race = random.choice(Character.RACES)
        
        # 클래스 선택
        print("\nAvailable Classes:")
        for i, char_class in enumerate(Character.CLASSES, 1):
            print(f"  {i}. {char_class}")
        
        print("\nSelect class (or press Enter for random): ", end="")
        class_input = input().strip()
        if class_input and class_input.isdigit() and 1 <= int(class_input) <= len(Character.CLASSES):
            char_class = Character.CLASSES[int(class_input) - 1]
        else:
            char_class = random.choice(Character.CLASSES)
        
        # 스탯 롤링
        print("\nRolling stats...")
        stats = None
        while True:
            stats = Character.roll_stats()
            print("\nStats rolled:")
            for stat, value in stats.items():
                print(f"  {stat}: {value:2d}")
            
            total = sum(stats.values())
            print(f"  Total: {total}")
            
            print("\nAccept these stats? (y/n/Enter=yes): ", end="")
            choice = input().strip().lower()
            if choice in ('', 'y', 'yes'):
                break
        
        # 캐릭터 생성
        character = Character(name, race, char_class, stats)
        
        print(f"\n{name} the {race} {char_class} is ready for adventure!")
        print("Press Enter to begin your quest...")
        input()
        
        return character
    
    def display_character_info(self, character):
        """캐릭터 정보 표시"""
        print(f"\n{character.name} the {character.race} {character.char_class}")
        print(f"Level: {character.level}  Gold: {character.gold}")
        print(f"Stats: STR:{character.stats['STR']} CON:{character.stats['CON']} "
              f"DEX:{character.stats['DEX']} INT:{character.stats['INT']} "
              f"WIS:{character.stats['WIS']} CHA:{character.stats['CHA']}")
    
    def display_game_state(self):
        """게임 상태 표시"""
        gs = self.game_state
        char = gs.character
        
        self.clear_screen()
        self.print_header(f"PROGRESS QUEST - {char.name} Lv.{char.level}")
        
        # 캐릭터 정보
        self.display_character_info(char)
        
        # 현재 Act 정보
        print(f"\n{gs.current_act.name}")
        
        # 진행 상태
        print("\n" + "-" * 70)
        self.print_progress_bar("Act Progress", gs.get_act_progress())
        self.print_progress_bar("Experience", gs.get_exp_progress())
        self.print_progress_bar("Encumbrance", gs.get_encumbrance_progress())
        
        # 통계
        print("\n" + "-" * 70)
        print(f"Kills: {gs.total_kills}  |  Quests: {gs.total_quests}")
        
        # 장비
        print("\nEquipment:")
        for slot, item in char.equipment.items():
            if item:
                print(f"  {slot.capitalize():10s}: {item}")
        
        # 주문 (최대 5개만 표시)
        if char.spells:
            print("\nSpells:")
            for spell in char.spells[-5:]:
                print(f"  - {spell}")
        
        print("\n" + "=" * 70)
    
    def run_task_with_timer(self, task):
        """태스크를 시간 지연과 함께 실행"""
        duration = task.duration
        task_name = task.name
        
        print(f"\n{task_name}")
        
        # 진행 바 표시
        for i in range(duration):
            remaining = duration - i
            progress = i / duration
            filled = int(30 * progress)
            bar = "█" * filled + "░" * (30 - filled)
            
            # \r을 사용해서 같은 줄에 업데이트
            print(f"\rProgress: [{bar}] {remaining}s remaining...", end="", flush=True)
            time.sleep(1)
        
        # 완료
        print(f"\r✓ Complete! [{'':<30s}]                    ")
    
    def run_game(self):
        """게임 메인 루프"""
        # 캐릭터 생성
        character = self.create_character()
        
        # 게임 상태 초기화
        self.game_state = GameState(character, max_depth=2)
        
        # 메인 게임 루프
        try:
            while True:
                # 화면 갱신
                self.display_game_state()
                
                # 현재 태스크 가져오기
                current_task = self.game_state.get_current_task()
                
                # 태스크 실행
                self.run_task_with_timer(current_task)
                
                # 태스크 완료 처리
                self.game_state.complete_current_task()
                
                # 짧은 딜레이
                time.sleep(0.5)
        
        except KeyboardInterrupt:
            print("\n\nGame interrupted by user.")
            print(f"Final Stats - Level: {character.level}, Kills: {self.game_state.total_kills}, Quests: {self.game_state.total_quests}")
            sys.exit(0)
    
    def start(self):
        """게임 시작"""
        self.print_header("Welcome to PROGRESS QUEST!")
        print("\nA zero-player RPG where your character plays itself!")
        print("Press Ctrl+C anytime to quit.\n")
        print("Press Enter to start...")
        input()
        
        self.run_game()


# 모듈로 import할 때를 대비한 처리
import random
