hdfsdll.so: hdfsdll.o
	g++ -shared -o hdfsdll.so hdfsdll.o -lhdfs

hdfsdll.o: hdfsdll.cpp
	g++ -o hdfsdll.o -I./include -I/usr/java/jdk1.6.0_15/include/ -I/usr/java/jdk1.6.0_15/include/linux/ -c hdfsdll.cpp
