"""Target only the launched player's X11 window; never inspect unrelated window text."""
import ctypes as c
import ctypes.util
import time


def prepare(pid, timeout=20):
    x = c.CDLL(ctypes.util.find_library('X11'))
    x.XOpenDisplay.argtypes = [c.c_char_p]
    x.XOpenDisplay.restype = c.c_void_p
    x.XDefaultRootWindow.argtypes = [c.c_void_p]
    x.XDefaultRootWindow.restype = c.c_ulong
    x.XInternAtom.argtypes = [c.c_void_p, c.c_char_p, c.c_int]
    x.XInternAtom.restype = c.c_ulong
    x.XQueryTree.argtypes = [c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_ulong), c.POINTER(c.POINTER(c.c_ulong)), c.POINTER(c.c_uint)]
    x.XGetWindowProperty.argtypes = [c.c_void_p, c.c_ulong, c.c_ulong, c.c_long, c.c_long, c.c_int, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_int), c.POINTER(c.c_ulong), c.POINTER(c.c_ulong), c.POINTER(c.POINTER(c.c_ubyte))]
    x.XFree.argtypes = [c.c_void_p]
    x.XGetGeometry.argtypes = [c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_uint), c.POINTER(c.c_uint), c.POINTER(c.c_uint), c.POINTER(c.c_uint)]
    x.XMoveResizeWindow.argtypes = [c.c_void_p, c.c_ulong, c.c_int, c.c_int, c.c_uint, c.c_uint]
    x.XMapRaised.argtypes = [c.c_void_p, c.c_ulong]
    x.XRaiseWindow.argtypes = [c.c_void_p, c.c_ulong]
    x.XSync.argtypes = [c.c_void_p, c.c_int]
    x.XCloseDisplay.argtypes = [c.c_void_p]
    display = x.XOpenDisplay(b':1')
    if not display:
        raise RuntimeError('Cannot open authorized display :1')
    try:
        root = x.XDefaultRootWindow(display)
        atom = x.XInternAtom(display, b'_NET_WM_PID', 0)
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            root_return, parent = c.c_ulong(), c.c_ulong()
            children, count = c.POINTER(c.c_ulong)(), c.c_uint()
            x.XQueryTree(display, root, c.byref(root_return), c.byref(parent), c.byref(children), c.byref(count))
            windows = [children[i] for i in range(count.value)]
            if children:
                x.XFree(children)
            for window in windows:
                kind, remaining, items = c.c_ulong(), c.c_ulong(), c.c_ulong()
                format_ = c.c_int()
                data = c.POINTER(c.c_ubyte)()
                status = x.XGetWindowProperty(display, window, atom, 0, 1, 0, 6, c.byref(kind), c.byref(format_), c.byref(items), c.byref(remaining), c.byref(data))
                owner = c.cast(data, c.POINTER(c.c_ulong))[0] if status == 0 and items.value and format_.value == 32 else None
                if data:
                    x.XFree(data)
                if owner != pid:
                    continue
                geometry_root, left, top = c.c_ulong(), c.c_int(), c.c_int()
                width, height, border, depth = c.c_uint(), c.c_uint(), c.c_uint(), c.c_uint()
                x.XGetGeometry(display, window, c.byref(geometry_root), c.byref(left), c.byref(top), c.byref(width), c.byref(height), c.byref(border), c.byref(depth))
                if width.value < 640 or height.value < 360:
                    continue
                x.XMoveResizeWindow(display, window, 0, 0, 1920, 1080)
                x.XMapRaised(display, window)
                x.XRaiseWindow(display, window)
                x.XSync(display, 0)
                time.sleep(0.5)
                return int(window)
            time.sleep(0.1)
        raise RuntimeError('Launched player has no owned X11 window')
    finally:
        x.XCloseDisplay(display)
