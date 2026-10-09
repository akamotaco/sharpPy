"""Known gap: retained module locals must observe later namespace writes."""
namespace = locals()
created_after_observation = 7
assert namespace['created_after_observation'] == 7
del created_after_observation
assert 'created_after_observation' not in namespace
print('module locals live: PASS')
