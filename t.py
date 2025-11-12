def f_x(x):
    return x * 2


class PagePub():
    def __init__(self):
        self._current_page = None
        self._log_text = {}

    def send_message(self, message):
        # Log(f's:{sender}/m:{message}/p:{param}')
        if message == 'set_focus_page':
            self.a = 1
        elif message == 'new_game':
            self.a = 1
        elif message == 'pub':
            self.a = 1
        elif message == 'npc':
            self.a = 1
        else:
            self.a = 1

    def update_pub_text(self, text:str):
        self._log_text['pub'] = text
        if self._current_page == 'pub':
            print('asdf')



print(globals())
print(f_x(10))  # This will call the function defined above