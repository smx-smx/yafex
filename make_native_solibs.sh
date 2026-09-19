#!/bin/bash
EPK2EXTRACT=$HOME/epk2extract
OUTDIR=Yafex/bin/Debug/net5.0

gcc -shared -nostdlib \
	-o ${OUTDIR}/squashfs.dll \
	-L${EPK2EXTRACT}/build_cygwin/obj/src/squashfs \
	-Wl,-whole-archive -lsquashfs \
	-Wl,-no-whole-archive \
	-llzo2 -lz \
	-lgcc -lc -lcygwin -lkernel32
