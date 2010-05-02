CFLAGS := `pkg-config --cflags mono`
LFLAGS := `pkg-config --libs mono`

GCC := gcc $(CFLAGS) -Wall -O2 -g

OPENSIMDLLDIR ?= ../../../bin

default: csfiles $(OPENSIMDLLDIR)/xmrengshare.dll $(OPENSIMDLLDIR)/xmrhelpers.so

# ------------------------------------------------------------------------------
#
# Create files needed for OpenSim
#
$(OPENSIMDLLDIR)/xmrengshare.dll: $(wildcard ../*/*.cx) $(wildcard ../*/*.h)
	make -C ../Compiler $(OPENSIMDLLDIR)/xmrengshare.dll

csfiles: $(patsubst %.cx,%.cs,$(wildcard *.cx)) MMRScriptCompile.cs

MMRScriptCompile.cs: ../Compiler/MMRScriptCompile.cx
	cpp -I../Compiler -C -P -nostdinc -o $@ $<

%.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

xmrhelpers: $(OPENSIMDLLDIR)/xmrhelpers.so

$(OPENSIMDLLDIR)/xmrhelpers.so: xmrhelpers.o
	ld -share -g -o $(OPENSIMDLLDIR)/xmrhelpers.so xmrhelpers.o

xmrhelpers.o: xmrhelpers.c
	$(GCC) -c -o xmrhelpers.o xmrhelpers.c

# ------------------------------------------------------------------------------

