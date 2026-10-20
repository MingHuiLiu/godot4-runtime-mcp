#!/usr/bin/env python3
import json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
s=(root/'addons/godot_mcp/GodotMcpServer.cs').read_text()
s=s[s.index('private static readonly List<McpToolDef> _tools'):s.index('// ===================== Godot Lifecycle')]
tools=[]
for m in re.finditer(r'new McpToolDef\s*\{',s):
    start=m.end(); depth=1; i=start; quoted=False; escape=False
    while depth and i<len(s):
        c=s[i]
        if quoted:
            if escape: escape=False
            elif c=="\\": escape=True
            elif c=='"': quoted=False
        elif c=='"': quoted=True
        elif c=='{': depth+=1
        elif c=='}': depth-=1
        i+=1
    b=s[start:i-1]
    name=re.search(r'Name\s*=\s*"([^"]+)"',b)
    desc=re.search(r'Description\s*=\s*"((?:[^"\\]|\\.)*)"',b)
    if not name or not desc: raise ValueError('Could not parse tool')
    props={}
    for x in re.finditer(r'\["(\w+)"\]\s*=\s*new Dictionary<string, object>\s*\{([^{}]*)\}',b,re.S):
        key,v=x.groups()
        kind=re.search(r'\["type"\]\s*=\s*"(\w+)"',v)
        label=re.search(r'\["description"\]\s*=\s*"((?:[^"\\]|\\.)*)"',v)
        if kind:
            props[key]={'type':kind.group(1)}
            if label: props[key]['description']=label.group(1)
    required=re.search(r'\["required"\]\s*=\s*new List<string>\s*\{([^}]+)\}',b,re.S)
    schema={'type':'object','properties':props}
    if required: schema['required']=re.findall(r'"([^"]+)"',required.group(1))
    if name.group(1)=='set_property': schema['properties']['value']={'description':'Any JSON value'}
    tools.append({'name':name.group(1),'description':desc.group(1),'inputSchema':schema})
if len(tools)<40 or len({t['name'] for t in tools})!=len(tools): raise ValueError('Invalid tool manifest')
(root/'runtime/tools.json').write_text(json.dumps(tools,ensure_ascii=False,indent=2)+'\n')
print('Exported',len(tools),'tools')
