import os
with open('res://project.godot') as f:
    text = f.read()
print('res read', text.startswith('config_version'), len(text) > 0)
with open('res://probe.tscn', 'rb') as f:
    b = f.read(4)
print('res binary', b[:1] == b'[')
p = 'user://round109.tmp'
with open(p, 'w') as f:
    f.write('가나다abc')
with open(p, encoding='utf-8') as f:
    print('user roundtrip', f.read())
print('user exists', os.path.exists(p))
os.remove(p)
print('removed', not os.path.exists(p))
# Run inside the CompatibilityProbe Godot project (GODOT define build):
#   Godot --headless --path <probe> -- --fixture resio_check.py
