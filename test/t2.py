def s(message):
    if message == 'set_focus_page':
        return 1
    elif message == 'new_game':
        return 2
    elif message == 'npc':
        return 4
    else:
        return 5
print(s('a'))
