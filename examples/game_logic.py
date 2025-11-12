"""
Progress Quest Game Logic
게임 로직과 데이터 구조를 담당
"""
import random
import time


# ============================================================================
# 랜덤 명칭 생성기
# ============================================================================

class NameGenerator:
    """랜덤 명칭 생성을 위한 클래스"""
    
    PREFIXES = [
        "Dark", "Ancient", "Mystic", "Flaming", "Frozen", "Thunder", "Shadow",
        "Glowing", "Cursed", "Blessed", "Mighty", "Swift", "Golden", "Silver",
        "Crystal", "Iron", "Blood", "Storm", "Dire", "Grand"
    ]
    
    ADJECTIVES = [
        "Ferocious", "Deadly", "Vicious", "Rabid", "Enraged", "Wild", "Mad",
        "Crazed", "Furious", "Savage", "Brutal", "Fierce", "Raging", "Angry",
        "Vengeful", "Twisted", "Corrupt", "Evil", "Demonic", "Infernal"
    ]
    
    MONSTERS = [
        "Goblin", "Orc", "Troll", "Dragon", "Wyvern", "Basilisk", "Chimera",
        "Hydra", "Manticore", "Gryphon", "Kobold", "Gnoll", "Bugbear", "Ogre",
        "Giant", "Demon", "Devil", "Wraith", "Skeleton", "Zombie", "Lich",
        "Beholder", "Mind Flayer", "Gelatinous Cube", "Rust Monster", "Owlbear"
    ]
    
    ITEMS = [
        "Sword", "Axe", "Mace", "Hammer", "Spear", "Dagger", "Staff", "Wand",
        "Bow", "Crossbow", "Shield", "Helm", "Armor", "Boots", "Gloves",
        "Ring", "Amulet", "Belt", "Cloak", "Robe", "Potion", "Scroll"
    ]
    
    QUEST_TYPES = [
        "Exterminate", "Seek", "Deliver", "Placate", "Investigate", "Rescue",
        "Protect", "Escort", "Destroy", "Retrieve", "Hunt", "Explore"
    ]
    
    LOCATIONS = [
        "Cave", "Dungeon", "Tower", "Castle", "Forest", "Mountain", "Swamp",
        "Desert", "Ruins", "Crypt", "Temple", "Fortress", "Lair", "Den",
        "Cavern", "Citadel", "Stronghold", "Keep", "Catacomb", "Tomb"
    ]
    
    @classmethod
    def generate_monster_name(cls):
        """몬스터 이름 생성"""
        adj = random.choice(cls.ADJECTIVES)
        monster = random.choice(cls.MONSTERS)
        return f"{adj} {monster}"
    
    @classmethod
    def generate_item_name(cls):
        """아이템 이름 생성"""
        prefix = random.choice(cls.PREFIXES)
        item = random.choice(cls.ITEMS)
        suffix = random.choice(["of Power", "of Doom", "of Glory", "of Might",
                               "of Wisdom", "of Speed", "of the Ancients"])
        return f"{prefix} {item} {suffix}"
    
    @classmethod
    def generate_quest_name(cls):
        """퀘스트 이름 생성"""
        quest_type = random.choice(cls.QUEST_TYPES)
        adj = random.choice(cls.ADJECTIVES)
        target = random.choice(cls.MONSTERS)
        location = random.choice(cls.LOCATIONS)
        
        templates = [
            f"{quest_type} the {adj} {target}",
            f"{quest_type} {random.randint(3, 20)} {target}s",
            f"{quest_type} the {adj} {target} in the {location}",
        ]
        return random.choice(templates)


# ============================================================================
# Task 계층 구조
# ============================================================================

class Task:
    """계층적 Task 구조를 위한 클래스"""
    
    def __init__(self, name, duration, depth=0, max_depth=2):
        self.name = name
        self.duration = duration  # 초 단위
        self.depth = depth
        self.max_depth = max_depth
        self.subtasks = []
        self.completed = False
        self.current_subtask_index = 0
        
        # depth가 허용 범위 내면 subtask 생성
        if depth < max_depth:
            num_subtasks = random.randint(2, 5)
            for _ in range(num_subtasks):
                subtask = self._create_subtask()
                self.subtasks.append(subtask)
    
    def _create_subtask(self):
        """하위 태스크 생성"""
        if self.depth == 0:  # Act -> Quest
            name = NameGenerator.generate_quest_name()
            duration = random.randint(10, 30)
        elif self.depth == 1:  # Quest -> Combat
            name = f"Fighting {NameGenerator.generate_monster_name()}"
            duration = random.randint(3, 8)
        else:  # Combat -> Kill
            name = f"Slaying {random.choice(['minion', 'creature', 'beast', 'foe'])}"
            duration = random.randint(1, 3)
        
        return Task(name, duration, self.depth + 1, self.max_depth)
    
    def get_current_task(self):
        """현재 진행 중인 가장 깊은 태스크 반환"""
        if not self.subtasks or self.current_subtask_index >= len(self.subtasks):
            return self
        
        current_subtask = self.subtasks[self.current_subtask_index]
        return current_subtask.get_current_task()
    
    def advance(self):
        """태스크 진행 (하위 태스크가 완료되면 다음으로)"""
        if not self.subtasks:
            self.completed = True
            return True
        
        current_subtask = self.subtasks[self.current_subtask_index]
        if current_subtask.advance():
            self.current_subtask_index += 1
            if self.current_subtask_index >= len(self.subtasks):
                self.completed = True
                return True
        return False
    
    def get_progress(self):
        """전체 진행도 (0.0 ~ 1.0)"""
        if not self.subtasks:
            return 1.0 if self.completed else 0.0
        
        if self.current_subtask_index >= len(self.subtasks):
            return 1.0
        
        completed = self.current_subtask_index
        current_progress = self.subtasks[self.current_subtask_index].get_progress()
        return (completed + current_progress) / len(self.subtasks)


# ============================================================================
# Character
# ============================================================================

class Character:
    """캐릭터 클래스"""
    
    RACES = [
        "Human", "Elf", "Dwarf", "Halfling", "Orc", "Troll",
        "Land Squid", "Talking Pony", "Double Wookiee", "Panda Man",
        "Enchanted Motorcycle", "Demicanadian"
    ]
    
    CLASSES = [
        "Fighter", "Wizard", "Cleric", "Rogue", "Ranger", "Paladin",
        "Puma Burglar", "Tickle-Mimic", "Tongueblade", "Bastard Lunatic",
        "Mu Monk", "Robot Monk"
    ]
    
    def __init__(self, name, race, char_class, stats):
        self.name = name
        self.race = race
        self.char_class = char_class
        self.stats = stats  # {'STR': x, 'CON': x, ...}
        self.level = 1
        self.exp = 0
        self.exp_to_next = 100
        self.gold = 0
        self.inventory = []
        self.equipment = {
            'weapon': 'Rusty Dagger',
            'armor': 'Tattered Rags',
            'helm': None,
            'boots': None,
            'gloves': None,
        }
        self.spells = []
        self.encumbrance = 0
        self.max_encumbrance = self.stats['STR'] + 10
    
    def gain_exp(self, amount):
        """경험치 획득"""
        self.exp += amount
        if self.exp >= self.exp_to_next:
            self.level_up()
    
    def level_up(self):
        """레벨업"""
        self.level += 1
        self.exp = 0
        self.exp_to_next = int(self.exp_to_next * 1.5)
        
        # 스탯 증가
        for stat in self.stats:
            self.stats[stat] += random.randint(1, 3)
        
        # 새 주문 획득
        spell = f"Spell of {random.choice(['Fire', 'Ice', 'Lightning', 'Death', 'Life', 'Chaos'])}"
        self.spells.append(spell)
        
        self.max_encumbrance = self.stats['STR'] + 10
    
    def add_loot(self, item):
        """전리품 추가"""
        self.inventory.append(item)
        self.encumbrance += random.randint(1, 5)
    
    def sell_loot(self):
        """전리품 판매"""
        for item in self.inventory:
            self.gold += random.randint(5, 20)
        self.inventory.clear()
        self.encumbrance = 0
    
    def buy_equipment(self):
        """장비 구매"""
        if self.gold > 50:
            slot = random.choice(list(self.equipment.keys()))
            self.equipment[slot] = NameGenerator.generate_item_name()
            self.gold -= random.randint(30, 50)
    
    @staticmethod
    def roll_stats():
        """스탯 롤링"""
        return {
            'STR': random.randint(3, 18),
            'CON': random.randint(3, 18),
            'DEX': random.randint(3, 18),
            'INT': random.randint(3, 18),
            'WIS': random.randint(3, 18),
            'CHA': random.randint(3, 18),
        }


# ============================================================================
# GameState
# ============================================================================

class GameState:
    """게임 상태 관리"""
    
    def __init__(self, character, max_depth=2):
        self.character = character
        self.max_depth = max_depth
        self.current_act = None
        self.act_number = 1
        self.total_kills = 0
        self.total_quests = 0
        self.start_new_act()
    
    def start_new_act(self):
        """새로운 Act 시작"""
        name = f"Act {self.act_number}: The {random.choice(['Rise', 'Fall', 'Return', 'Doom'])} of {random.choice(['Darkness', 'Heroes', 'Evil', 'Glory'])}"
        self.current_act = Task(name, 0, depth=0, max_depth=self.max_depth)
        self.act_number += 1
    
    def get_current_task(self):
        """현재 진행 중인 태스크"""
        return self.current_act.get_current_task()
    
    def complete_current_task(self):
        """현재 태스크 완료"""
        current = self.get_current_task()
        
        # 몬스터 처치 시 보상
        if "Fighting" in current.name or "Slaying" in current.name:
            self.total_kills += 1
            self.character.gain_exp(random.randint(10, 30))
            self.character.add_loot(NameGenerator.generate_item_name())
        
        # 퀘스트 완료 시 보상
        if current.depth == 1:
            self.total_quests += 1
            self.character.gold += random.randint(50, 100)
            if random.random() < 0.3:  # 30% 확률로 장비 획득
                slot = random.choice(list(self.character.equipment.keys()))
                self.character.equipment[slot] = NameGenerator.generate_item_name()
        
        # Act 진행
        if self.current_act.advance():
            self.start_new_act()
        
        # 인벤토리가 가득 차면 판매
        if self.character.encumbrance >= self.character.max_encumbrance:
            self.character.sell_loot()
            self.character.buy_equipment()
    
    def get_act_progress(self):
        """Act 진행도"""
        return self.current_act.get_progress()
    
    def get_exp_progress(self):
        """경험치 진행도"""
        return self.character.exp / self.character.exp_to_next
    
    def get_encumbrance_progress(self):
        """인벤토리 진행도"""
        return self.character.encumbrance / self.character.max_encumbrance
