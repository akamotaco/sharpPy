#!/usr/bin/env python3
"""
Progress Quest Demo Script
자동으로 캐릭터를 생성하고 잠시 게임을 실행하는 데모
"""

import sys
import time
from game_logic import Character, GameState, NameGenerator


def demo():
    """데모 실행"""
    print("=" * 70)
    print(" PROGRESS QUEST - DEMO MODE")
    print("=" * 70)
    print("\nCreating a random character...\n")
    
    # 랜덤 캐릭터 생성
    import random
    name = f"Hero{random.randint(1000, 9999)}"
    race = random.choice(Character.RACES)
    char_class = random.choice(Character.CLASSES)
    stats = Character.roll_stats()
    
    print(f"Name: {name}")
    print(f"Race: {race}")
    print(f"Class: {char_class}")
    print("\nStats:")
    for stat, value in stats.items():
        print(f"  {stat}: {value}")
    
    character = Character(name, race, char_class, stats)
    
    print("\n" + "=" * 70)
    print("Starting adventure... (will run for 30 seconds)")
    print("=" * 70)
    
    # 게임 상태 생성
    game_state = GameState(character, max_depth=2)
    
    # 30초 동안 실행
    start_time = time.time()
    task_count = 0
    
    try:
        while time.time() - start_time < 30:
            current_task = game_state.get_current_task()
            
            print(f"\n[Task {task_count + 1}] {current_task.name}")
            print(f"  Duration: {current_task.duration}s")
            
            # 진행 바 표시
            for i in range(current_task.duration):
                remaining = current_task.duration - i
                progress = i / current_task.duration
                filled = int(30 * progress)
                bar = "█" * filled + "░" * (30 - filled)
                
                print(f"\r  Progress: [{bar}] {remaining}s remaining...", end="", flush=True)
                time.sleep(1)
            
            print(f"\r  ✓ Complete! [{'':<30s}]                    ")
            
            # 태스크 완료
            game_state.complete_current_task()
            task_count += 1
            
            # 상태 출력
            print(f"  Level: {character.level} | XP: {character.exp}/{character.exp_to_next} | "
                  f"Gold: {character.gold} | Kills: {game_state.total_kills}")
    
    except KeyboardInterrupt:
        print("\n\nDemo interrupted.")
    
    # 최종 통계
    print("\n" + "=" * 70)
    print(" DEMO COMPLETE - Final Statistics")
    print("=" * 70)
    print(f"Character: {character.name} the {character.race} {character.char_class}")
    print(f"Level: {character.level}")
    print(f"Total Kills: {game_state.total_kills}")
    print(f"Quests Completed: {game_state.total_quests}")
    print(f"Gold: {character.gold}")
    print(f"Tasks Completed: {task_count}")
    print(f"\nCurrent Equipment:")
    for slot, item in character.equipment.items():
        if item:
            print(f"  {slot.capitalize()}: {item}")
    
    if character.spells:
        print(f"\nSpells Learned: {len(character.spells)}")
        for spell in character.spells[-3:]:
            print(f"  - {spell}")
    
    print("\n" + "=" * 70)


if __name__ == "__main__":
    demo()
