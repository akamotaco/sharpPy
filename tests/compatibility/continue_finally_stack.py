events = []
def loop():
    total = 0
    for i in range(5):
        try:
            if i == 1:
                continue
                events.append("dead")
            if i == 3:
                break
                events.append("dead")
            total += i
        finally:
            events.append(i)
    return total

print(loop(), events)
