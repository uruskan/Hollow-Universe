"""Small original drafting marks painted into procedural game texture arrays."""
import math
import numpy as np


class Diagram:
    def __init__(self,pixels):
        self.pixels=pixels
        self.height,self.width=pixels.shape[:2]

    def line(self,a,b,color=(.60,.79,.86),width=.0015,opacity=1):
        ax,ay=a;bx,by=b;h,w=self.height,self.width
        x0=max(0,int((min(ax,bx)-width*2)*w));x1=min(w,int((max(ax,bx)+width*2)*w)+1)
        y0=max(0,int((min(ay,by)-width*2)*h));y1=min(h,int((max(ay,by)+width*2)*h)+1)
        if x1<=x0 or y1<=y0:return
        yy,xx=np.mgrid[y0:y1,x0:x1].astype(np.float32);xx=(xx+.5)/w;yy=(yy+.5)/h
        dx,dy=bx-ax,by-ay;t=np.clip(((xx-ax)*dx+(yy-ay)*dy)/max(dx*dx+dy*dy,1e-9),0,1)
        distance=np.hypot(xx-ax-t*dx,yy-ay-t*dy)
        mask=np.clip((width-distance)*min(w,h)+.5,0,1)*opacity
        tile=self.pixels[y0:y1,x0:x1,:3]
        tile[:]=tile*(1-mask[:,:,None])+np.array(color)*mask[:,:,None]

    def path(self,points,color=(.60,.79,.86),width=.0015,closed=False,opacity=1):
        for a,b in zip(points,points[1:]+([points[0]] if closed else [])):
            self.line(a,b,color,width,opacity)

    def box(self,x,y,w,h,**kwargs):
        self.path([(x,y),(x+w,y),(x+w,y+h),(x,y+h)],closed=True,**kwargs)

    def circle(self,x,y,rx,ry=None,**kwargs):
        ry=ry or rx
        self.path([(x+math.cos(i*math.tau/64)*rx,y+math.sin(i*math.tau/64)*ry) for i in range(64)],closed=True,**kwargs)

    def dimension(self,a,b,**kwargs):
        self.line(a,b,**kwargs)
        dx,dy=b[0]-a[0],b[1]-a[1];length=max(math.hypot(dx,dy),.001)
        dx,dy=dx/length*.009,dy/length*.009
        for x,y in [a,b]:self.line((x-dy-dx*.6,y+dx-dy*.6),(x+dy+dx*.6,y-dx+dy*.6),**kwargs)

    def schematic(self):
        """An invented service drawing: housing, circular insert and connectors."""
        c=(.60,.78,.83)
        self.path([(.22,.57),(.22,.29),(.30,.23),(.67,.23),(.75,.31),(.75,.57),(.65,.64),(.30,.64)],c,.0025,True)
        self.box(.29,.31,.38,.24,color=c,width=.002)
        for radius in (.091,.064):self.circle(.48,.43,radius,radius*.82,color=c,width=.002)
        self.line((.35,.43),(.61,.43),c,.001)
        self.line((.48,.33),(.48,.53),c,.001)
        for x in (.32,.64):
            for y in (.33,.53):self.circle(x,y,.008,.006,color=c,width=.0016)
        for x in (.27,.38,.58,.69):self.box(x,.18,.027,.046,color=c,width=.0013)
        self.dimension((.18,.23),(.18,.64),color=c,width=.0012)
        self.dimension((.22,.69),(.75,.69),color=c,width=.0012)
        self.path([(.60,.35),(.80,.29),(.89,.29)],c,.0012)
        self.path([(.34,.58),(.26,.74),(.13,.74)],c,.0012)
        for row in range(4):
            self.line((.59,.77+row*.018),(.87-row*.025,.77+row*.018),c,.0011,.7)
        self.box(.58,.74,.30,.12,color=c,width=.0012)
