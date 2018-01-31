// change all IL addresses in .xmrasm files to .... so diff can compare them
//  cc -O2 -o modxmrasm modxmrasm.c

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
int main()
{
	char line[1024], *p;
	unsigned long x;

	while (fgets (line, sizeof line, stdin) != NULL) {
		if (line[0] != ' ') goto print;
		if (line[1] != ' ') goto print;
		x = strtoul (line + 2, &p, 16);
		if (p != line + 6) goto print;
		memset (line + 2, '.', 4);
	    print:
		fputs (line, stdout);
	}
	return 0;
}
