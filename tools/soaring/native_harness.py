"""Bindings derived from the production C header, never substitute dynamics."""
import ctypes as C
import os
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / 'QuestFlight/Packages/com.alexoviedo.questflight.jsbsim'
DATA = PACKAGE / 'RuntimeData/JSBSim-1.3.1'
HEADER = ROOT / 'tools/native/jsbsim_bridge/include/qfl_jsbsim_native.h'
text = re.sub(r'/\*.*?\*/', '', HEADER.read_text(), flags=re.S)
TYPES = {}
for name, body in re.findall(r'typedef struct (\w+) \{(.*?)\}', text, re.S):
    fields = []
    for kind, names in re.findall(r'\b(double|int)\s+([^;]+);', body):
        fields.extend((n.strip(), C.c_double if kind == 'double' else C.c_int) for n in names.split(','))
    TYPES[name.removeprefix('qfl_jsbsim_')] = type(name, (C.Structure,), {'_fields_': fields})

def values(struct):
    return {n: getattr(struct, n) for n, _ in struct._fields_}

class Bridge:
    def __init__(self, library=None, dependency=None):
        library = Path(library or PACKAGE / 'Runtime/Plugins/Windows/x86_64/qfl_jsbsim_native.dll').resolve()
        self.cookies = [os.add_dll_directory(str(library.parent))]
        if dependency: self.cookies.append(os.add_dll_directory(str(Path(dependency).resolve())))
        self.library = library
        self.dll = C.CDLL(str(library))
        self.create = self.bind('create', [], C.c_void_p)
        self.destroy = self.bind('destroy', [C.c_void_p], None)
        self.error = self.bind('last_error', [C.c_void_p], C.c_char_p)
        self.load = self.bind('load_aircraft', [C.c_void_p,C.c_char_p,C.c_char_p])
        self.step = self.bind('advance',[C.c_void_p,C.c_double])
        self.version = self.bind('api_version',[])()
        for method, typename in [('reset','initial_conditions'),('set_controls','controls'),('set_atmosphere','atmosphere'),
                                 ('set_mass_configuration','mass_configuration'),('get_state','state'),('get_diagnostics','diagnostics')]:
            setattr(self, method, self.bind(method, [C.c_void_p,C.POINTER(TYPES[typename])]))
        if self.version >= 3:
            for method, typename in [('get_capabilities_v3','capabilities_v3'),('set_glider_controls_v3','glider_controls_v3'),
                ('set_external_load_v3','external_load_v3'),('set_conditions_v3','conditions_v3'),('get_air_data_v3','air_data_v3')]:
                setattr(self, method, self.bind(method,[C.c_void_p,C.POINTER(TYPES[typename])]))
            self.clear_loads = self.bind('clear_external_loads_v3',[C.c_void_p])
            self.attachment = self.bind('get_attachment_state_v3',[C.c_void_p,C.c_int,C.POINTER(TYPES['attachment_state_v3'])])
            self.contact = self.bind('get_contact_v3',[C.c_void_p,C.c_int,C.POINTER(TYPES['contact_v3'])])
            self.loading = self.bind('set_loading_v3',[C.c_void_p,C.POINTER(TYPES['station_load_v3']),C.c_int,C.POINTER(TYPES['station_load_v3']),C.c_int])
            self.terrain = self.bind('set_terrain_patch_v3',[C.c_void_p,C.POINTER(TYPES['terrain_patch_v3']),C.POINTER(C.c_double),C.c_int])

    def bind(self,name,args,result=C.c_int):
        f=getattr(self.dll,'qfl_jsbsim_'+name);f.argtypes=args;f.restype=result;return f
    def check(self,ok,h):
        if not ok: raise RuntimeError((self.error(h) or b'Native operation failed').decode())
    def read(self,h,method,typename):
        value=TYPES[typename]();self.check(getattr(self,method)(h,C.byref(value)),h);return value

class Session:
    def __init__(self,bridge,model='c172x',data=DATA):
        self.b=bridge;self.h=bridge.create()
        bridge.check(bridge.load(self.h,str(data).encode(),model.encode()),self.h)
    def __enter__(self):return self
    def __exit__(self,*args):self.b.destroy(self.h)
    def send(self,method,value):self.b.check(getattr(self.b,method)(self.h,C.byref(value)),self.h)
    def step(self,dt=1/120):self.b.check(self.b.step(self.h,dt),self.h)
    def state(self):return self.b.read(self.h,'get_state','state')
    def diagnostics(self):return self.b.read(self.h,'get_diagnostics','diagnostics')
    def reset(self,altitude=7288,cas=55,heading=90,pitch=0,gamma=-3,engine=0):
        self.send('reset',TYPES['initial_conditions'](40.03936527,-105.22608958,altitude,5288,cas,heading,pitch,0,gamma,engine))
