CFLAGS := `pkg-config --cflags mono`
LFLAGS := `pkg-config --libs mono`

GCC := gcc $(CFLAGS) -Wall -O2 -g

default: csfiles

# ------------------------------------------------------------------------------

csfiles: $(patsubst %.cx,%.cs,$(wildcard *.cx))

%.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

xmrhelpers: ../../../../opensim/bin/xmrhelpers.so

../../../../opensim/bin/xmrhelpers.so: xmrhelpers.so
	cp xmrhelpers.so ../../../../opensim/bin/

xmrhelpers.so: xmrhelpers.o
	ld -share -g -o xmrhelpers.so xmrhelpers.o

xmrhelpers.o: xmrhelpers.c
	$(GCC) -c -o xmrhelpers.o xmrhelpers.c

# ------------------------------------------------------------------------------

