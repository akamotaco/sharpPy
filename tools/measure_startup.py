"""What a bundle of marshalled code would save at startup, in a fresh process each time.

Modes, all run under SharpPy:
  list <out>         import the sample and write the source files it loaded
  imports            time the sample's imports from source
  compile <list>     time reading and compiling those files
  dump <list> <out>  write them as one marshalled list
  load <blob>        time reading that and rebuilding the code objects
"""
import marshal
import os
import sys
import time

SAMPLE = ("typing", "dataclasses", "enum", "re", "json", "collections", "threading")


def clock():
    return time.perf_counter()


def read_list(path):
    with open(path, encoding="utf-8") as stream:
        return [line.strip() for line in stream if line.strip()]


def source(path):
    with open(path, encoding="utf-8") as stream:
        return stream.read()


def main():
    mode = sys.argv[1]
    if mode == "list":
        for name in SAMPLE:
            __import__(name)
        files = sorted({m.__file__ for m in sys.modules.values()
                        if getattr(m, "__file__", None) and os.path.exists(m.__file__)})
        with open(sys.argv[2], "w", encoding="utf-8") as stream:
            stream.write("\n".join(files))
        print("files", len(files), "bytes", sum(len(source(f)) for f in files))
    elif mode == "imports":
        start = clock()
        for name in SAMPLE:
            __import__(name)
        print(round((clock() - start) * 1000, 1))
    elif mode == "compile":
        files = read_list(sys.argv[2])
        start = clock()
        for path in files:
            compile(source(path), path, "exec")
        print(round((clock() - start) * 1000, 1))
    elif mode == "dump":
        codes = [(path, compile(source(path), path, "exec")) for path in read_list(sys.argv[2])]
        with open(sys.argv[3], "wb") as stream:
            marshal.dump(codes, stream)
    elif mode == "load":
        start = clock()
        with open(sys.argv[2], "rb") as stream:
            data = stream.read()
        marshal.loads(data)
        print(round((clock() - start) * 1000, 1), len(data))


main()
