events = []
def exceptional():
    try:
        try:
            raise ValueError("live")
            events.append("dead")
        finally:
            events.append("inner")
    except ValueError as error:
        events.append(str(error))
        return 7
    finally:
        events.append("outer")
    events.append("dead")

print(exceptional(), events)
