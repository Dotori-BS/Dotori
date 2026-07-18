#include <cstdio>

int main(int argc, char** argv)
{
    std::printf("Dotori first-class test target passed (%d args).\n", argc - 1);
    return argv != nullptr ? 0 : 1;
}
