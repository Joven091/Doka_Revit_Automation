"""Independent nominal material-region and weld-volume reference examples."""
from decimal import Decimal as D, getcontext, ROUND_HALF_UP
from pathlib import Path
import json

getcontext().prec=100
ROOT=Path(__file__).parent
PI=D('3.141592653589793')
rows=[]
def clean(value):
    text=format(value,'f')
    return text.rstrip('0').rstrip('.') if '.' in text else text
def example(name,shape,section,q,length,edges,kind='fillet',share=D(1),pieces=1,cap_height='1',size='4'):
    q=D(q);length=D(length);count=D(pieces)
    gap=(q*D('.002')*D(share)).quantize(D('1e-24'),rounding=ROUND_HALF_UP) if kind=='butt' else D(0)
    area=(D(size)/1000)**2/2 if kind=='fillet' else D('.012')*(D(cap_height)/1000)/2
    extra=(area*(length/1000)*D(7850)+gap)*count
    steel=q*(D(2) if shape=='plate' else D(6))*count
    weld={'enabled':True,'type':kind,'L1':'1000' if shape=='plate' else '3000','L2':'1000' if shape=='plate' else '3000','z':size,'gapMm':'2','capWidthMm':'12','capHeightMm':cap_height,'faces':'1','edges':edges}
    rows.append({'name':name,'shape':shape,'input':{**section,'L':'999',**({'n':str(pieces)} if shape=='plate' else {}),'weld':weld},'lengthMm':clean(length),'steelTotalKg':clean(steel),'weldTotalKg':clean(extra),'gapPerJointKg':clean(gap),'totalKg':clean(steel+extra)})

flat={'W':'100','t':'10'}
off4={f'edge-{i}':'0' for i in range(4)}
example('flat-top-fillet','flat_bar',flat,'7.85',100,{**off4,'edge-0':'full'})
example('flat-partial-top-fillet','flat_bar',flat,'7.85',40,{**off4,'edge-0':'40'})
example('flat-top-full-wall-butt','flat_bar',flat,'7.85',100,{**off4,'edge-0':'full'},'butt')
example('flat-partial-top-butt','flat_bar',flat,'7.85',40,{**off4,'edge-0':'40'},'butt',D('.4'))
example('flat-opposite-full-sides-no-duplicate-gap','flat_bar',flat,'7.85',200,{**off4,'edge-0':'full','edge-2':'full'},'butt')
example('flat-overlapping-partial-sides-no-duplicate-gap','flat_bar',flat,'7.85',80,{**off4,'edge-0':'40','edge-2':'40'},'butt',D('.4'))
example('flat-short-tip-butt','flat_bar',flat,'7.85',10,{**off4,'edge-1':'full'},'butt',D('.1'))
example('plate-quantity-selected-top','plate',{'W':'1000','t':'10'},'78.5',1000,{**off4,'edge-0':'full'},'butt',pieces=2)
example('square-top-fills-whole-gap','square_bar',{'A':'20'},'3.14',20,{**off4,'edge-0':'full'},'butt')
example('square-opposite-sides-count-gap-once','square_bar',{'A':'20'},'3.14',40,{**off4,'edge-0':'full','edge-2':'full'},'butt')

chs_q=PI*D(4)*D('110.3')*D('.00785')
example('circular-quarter-fillet','chs',{'D':'114.3','t':'4'},chs_q,PI*D('114.3')/4,{**off4,'edge-0':'full'})
example('circular-quarter-butt','chs',{'D':'114.3','t':'4'},chs_q,PI*D('114.3')/4,{**off4,'edge-0':'full'},'butt',D('.25'))
example('circular-partial-arc-butt','chs',{'D':'114.3','t':'4'},chs_q,25,{**off4,'edge-0':'25'},'butt',D(25)/(PI*D('114.3')))

rhs_area=D(900)-(12-2*PI)*9
rhs_q=rhs_area*D('.00785')
off8={f'edge-{i}':'0' for i in range(8)}
example('rhs-top-flat-wall-butt','rhs',{'A':'100','B':'50','t':'3'},rhs_q,41,{**off8,'edge-0':'full'},'butt',D(123)/rhs_area)
example('rhs-rounded-corner-butt','rhs',{'A':'100','B':'50','t':'3'},rhs_q,PI*D('2.25'),{**off8,'edge-1':'full'},'butt',(PI*D('4.5'))/rhs_area)
example('rhs-partial-rounded-corner-butt','rhs',{'A':'100','B':'50','t':'3'},rhs_q,3,{**off8,'edge-1':'3'},'butt',D(6)/rhs_area)

ipe_area=2*D(100)*D('8.5')+D('5.6')*D(183)
off12={f'edge-{i}':'0' for i in range(12)}
example('ipe-partial-web-thickness','ipe',{'profile':'IPE 200'},'22.4',50,{**off12,'edge-3':'50'},'butt',(50*D('5.6'))/ipe_area,cap_height='0')
example('ipe-partial-flange-thickness','ipe',{'profile':'IPE 200'},'22.4',50,{**off12,'edge-0':'50'},'butt',(50*D('8.5'))/ipe_area,cap_height='0')
example('ipe-web-opposite-sides-overlap','ipe',{'profile':'IPE 200'},'22.4',100,{**off12,'edge-3':'50','edge-9':'50'},'butt',(50*D('5.6'))/ipe_area,cap_height='0')
(ROOT/'selection-vectors.json').write_text(json.dumps(rows,indent=2)+'\n')
print('Generated',len(rows),'independent selected-region vectors.')
