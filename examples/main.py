#!/usr/bin/env python3
"""
Progress Quest - Main Entry Point
순수 Python으로 구현된 Progress Quest 게임

Usage:
    python main.py
"""

from ui import ProgressQuestUI


def main():
    """메인 함수"""
    game = ProgressQuestUI()
    game.start()


if __name__ == "__main__":
    main()
