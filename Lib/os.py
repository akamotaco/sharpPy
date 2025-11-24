# Simplified os module for SharpPy
# Import the low-level nt module (C# implementation)
import nt

# Re-export commonly used functions from nt
stat = nt.stat
getcwd = nt.getcwd
chdir = nt.chdir
listdir = nt.listdir
mkdir = nt.mkdir
rmdir = nt.rmdir
remove = nt.remove
unlink = nt.unlink
rename = nt.rename
access = nt.access
getenv = nt.getenv
putenv = nt.putenv
fspath = nt.fspath
urandom = nt.urandom
environ = nt.environ

# Constants
F_OK = nt.F_OK
R_OK = nt.R_OK
W_OK = nt.W_OK
X_OK = nt.X_OK

# Platform information
name = 'nt'  # Windows

# os.path submodule
import ntpath as path
